using System;
using System.Collections.Generic;
using UnityEngine;

namespace AILevelGenerator.Runtime.Data
{
    [Serializable]
    public class LevelData
    {
        public string LevelName;
        public string Description;
        public Vector3 PlayerStartPosition;
        public List<PropPlacement> Props = new();
        public List<TaskData> Tasks = new();
        public TerrainData Terrain;
    }

    [Serializable]
    public class PropPlacement
    {
        public string PrefabLogicalName;    // 对应资源映射表的 Key
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Scale = Vector3.one; // 默认单位缩放，防止零缩放导致物体不可见
        /// <summary>巡逻路径点（Day2 战斗扩展，可选）：巡逻型敌人按序循环移动的点列；空 = 不巡逻。
        /// LLM Schema 可选输出 patrol_points；模板侧在未命中时按配置确定性兜底补齐（见 ConfigurableLevelTemplate）。</summary>
        public List<Vector3> PatrolPoints = new();
    }

    [Serializable]
    public class TerrainData
    {
        public int Width = 100;
        public int Length = 100;
        public float HeightScale = 10f;

    }
}