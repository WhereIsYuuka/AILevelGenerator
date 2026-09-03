using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace AILevelGenerator.Runtime.LLM
{
    /// <summary>
    /// 磁盘缓存（第五周-Day5，两级缓存的第二级）：按键落盘为独立文件，编辑器重启/域重载后仍可命中，
    /// 补足内存缓存（进程内 FIFO）无法跨会话存活的短板。纯 System.IO 实现（不引用 UnityEditor），目录可注入便于单测。
    /// - 目录注入（Func 每次实时取值）：目录被外部删除后下次写入自动重建（自愈）；
    /// - 首访懒加载索引：扫描目录 *.json 文件名（{key:X16}）建立键索引，内容按需读盘（磁盘缓存不常驻内存）；
    /// - 写入原子化：先写 {key}.tmp 再替换目标文件（防半写文件）；容量满按文件写入时间淘汰最旧；
    /// - 读损坏自愈：空文件/读取异常 → 删除该文件并降级 miss（不向生成链路抛错）；
    /// - 淘汰策略：容量上限（默认 512）按"最近写入"FIFO 淘汰（写入即刷新时间，与内存缓存语义一致）。
    /// 非线程安全（调度层保证串行调用）。
    /// </summary>
    public class DiskGenerationCache : IGenerationCache
    {
        public const int DefaultCapacity = 512;
        private const string FileExtension = ".json";

        private readonly Func<string> _directoryProvider;
        private readonly int _capacity;
        private readonly Dictionary<ulong, string> _index = new(); // 键 → 文件名
        private bool _indexLoaded;

        public DiskGenerationCache(string directoryPath, int capacity = DefaultCapacity)
            : this(() => directoryPath, capacity)
        {
        }

        public DiskGenerationCache(Func<string> directoryProvider, int capacity = DefaultCapacity)
        {
            _directoryProvider = directoryProvider ?? throw new ArgumentNullException(nameof(directoryProvider));
            _capacity = capacity < 1 ? 1 : capacity;
        }

        /// <summary> 当前缓存目录（由提供器实时解析；提供器为 null 时返回空串 → 全部操作降级 miss） </summary>
        private string DirectoryPath
        {
            get
            {
                var dir = _directoryProvider?.Invoke();
                return string.IsNullOrEmpty(dir) ? string.Empty : dir;
            }
        }

        /// <summary> 已索引条目数（懒加载后有效；目录被外部改动时与实际文件可能短暂不一致） </summary>
        public int Count => _index.Count;

        public bool TryGet(string templateId, int seed, string prompt, ulong templateDependencyHash, int schemaVersion, out string rawJson)
        {
            EnsureIndex();
            var key = GenerationCache.BuildKey(templateId, seed, prompt, templateDependencyHash, schemaVersion);
            if (!_index.TryGetValue(key, out var fileName))
            {
                rawJson = null;
                return false;
            }

            try
            {
                var path = Path.Combine(DirectoryPath, fileName);
                if (!File.Exists(path))
                {
                    _index.Remove(key); // 文件被外部删除：自愈索引
                    rawJson = null;
                    return false;
                }
                rawJson = File.ReadAllText(path);
                if (rawJson.Length == 0)
                {
                    DeleteQuietly(path);
                    _index.Remove(key);
                    rawJson = null;
                    return false; // 空文件（写入中断残留）：自愈删除，降级 miss
                }
                return true;
            }
            catch (IOException)
            {
                rawJson = null;
                return false; // 读盘异常：降级 miss（不阻断生成链路）
            }
            catch (UnauthorizedAccessException)
            {
                rawJson = null;
                return false;
            }
        }

        public void Put(string templateId, int seed, string prompt, ulong templateDependencyHash, int schemaVersion, string rawJson)
        {
            if (rawJson == null) return;
            EnsureIndex();

            var key = GenerationCache.BuildKey(templateId, seed, prompt, templateDependencyHash, schemaVersion);
            var dir = DirectoryPath;
            if (string.IsNullOrEmpty(dir)) return;

            try
            {
                Directory.CreateDirectory(dir); // 目录不存在自动重建（自愈）
                if (!_index.ContainsKey(key) && _index.Count >= _capacity)
                    EvictOldest(dir); // 满员先淘汰最旧，再写新条目

                var fileName = key.ToString("X16", CultureInfo.InvariantCulture) + FileExtension;
                var target = Path.Combine(dir, fileName);
                var temp = Path.Combine(dir, key.ToString("X16", CultureInfo.InvariantCulture) + ".tmp");

                File.WriteAllText(temp, rawJson); // 先写临时文件（内容完整才替换，防半写文件入库）
                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);

                _index[key] = fileName; // 同键覆盖会刷新文件写入时间（淘汰序随之更新；内容修复场景下刷新排位无害）
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[AI Generator] 磁盘缓存写入失败（降级为仅内存缓存）：{e.Message}");
            }
            catch (UnauthorizedAccessException e)
            {
                Debug.LogWarning($"[AI Generator] 磁盘缓存写入失败（降级为仅内存缓存）：{e.Message}");
            }
        }

        public void Clear()
        {
            var dir = DirectoryPath;
            _index.Clear();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            try
            {
                foreach (var file in Directory.GetFiles(dir, "*" + FileExtension))
                    DeleteQuietly(file);
                foreach (var file in Directory.GetFiles(dir, "*.tmp"))
                    DeleteQuietly(file);
            }
            catch (IOException)
            {
                // 清盘失败静默（下次写入自愈）
            }
        }

        /// <summary> 懒加载目录索引（首访执行一次；目录缺失/异常 → 空索引并告警一次） </summary>
        private void EnsureIndex()
        {
            if (_indexLoaded) return;
            _indexLoaded = true;

            var dir = DirectoryPath;
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                if (!Directory.Exists(dir)) return; // 尚未创建：空索引
                var files = Directory.GetFiles(dir, "*" + FileExtension);
                foreach (var file in files)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (ulong.TryParse(name, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var key))
                        _index[key] = Path.GetFileName(file);
                }
            }
            catch (IOException)
            {
                Debug.LogWarning("[AI Generator] 磁盘缓存索引加载失败（本次会话按空缓存处理）");
            }
            catch (UnauthorizedAccessException)
            {
                Debug.LogWarning("[AI Generator] 磁盘缓存目录不可访问（本次会话按空缓存处理）");
            }
        }

        /// <summary> 容量满时淘汰写入时间最旧的条目（扫描索引取最小 LastWriteTime；条目少、淘汰低频，线性扫描可接受） </summary>
        private void EvictOldest(string dir)
        {
            ulong oldestKey = 0;
            var oldestTime = DateTime.MaxValue;
            var hasCandidate = false;
            foreach (var kv in _index)
            {
                try
                {
                    var time = File.GetLastWriteTimeUtc(Path.Combine(dir, kv.Value));
                    if (time < oldestTime)
                    {
                        oldestTime = time;
                        oldestKey = kv.Key;
                        hasCandidate = true;
                    }
                }
                catch (IOException)
                {
                    // 单个文件读时间失败：跳过（可能已被外部删除，TryGet 时自愈）
                }
            }
            if (!hasCandidate) return;
            _index.Remove(oldestKey);
            DeleteQuietly(Path.Combine(dir, oldestKey.ToString("X16", CultureInfo.InvariantCulture) + FileExtension));
        }

        /// <summary> 删除文件并吞掉 IO 异常（自愈路径不向调用方抛错） </summary>
        private static void DeleteQuietly(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
