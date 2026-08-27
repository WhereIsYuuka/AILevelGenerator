using System;
using System.Collections.Generic;
using UnityEngine;

namespace AILevelGenerator.RunTime.Data
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
        public Vector3 Scale = Vector3.zero;
    }

    [Serializable]
    public class TerrainData
    {
        public int Width = 100;
        public int Length = 100;
        public float HeightScale = 10f;

    }
}