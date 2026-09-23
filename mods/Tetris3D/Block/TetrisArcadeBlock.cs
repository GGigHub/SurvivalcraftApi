using Engine;

namespace Game {
    public class TetrisArcadeBlock : CubeBlock {
        public static int Index;

        public override int GetFaceTextureSlot(int face, int value) {
            int data = Terrain.ExtractData(value);
            int front = data switch {
                0 => 0,
                1 => 1,
                2 => 2,
                _ => 3
            };
            if (face == 4 || face == 5) {
                return 42;
            }
            return face == front ? 27 : 62;
        }

        public override BlockPlacementData GetPlacementValue(SubsystemTerrain subsystemTerrain,
            ComponentMiner componentMiner,
            int value,
            TerrainRaycastResult raycastResult) {
            Vector3 forward = Matrix.CreateFromQuaternion(componentMiner.ComponentCreature.ComponentCreatureModel.EyeRotation).Forward;
            float num = Vector3.Dot(forward, Vector3.UnitZ);
            float num2 = Vector3.Dot(forward, Vector3.UnitX);
            float num3 = Vector3.Dot(forward, -Vector3.UnitZ);
            float num4 = Vector3.Dot(forward, -Vector3.UnitX);
            int data = num == MathUtils.Max(num, num2, num3, num4) ? 2 :
                num2 == MathUtils.Max(num2, num3, num4) ? 3 :
                num3 == Math.Max(num3, num4) ? 0 : 1;
            BlockPlacementData result = default;
            result.Value = Terrain.ReplaceData(Terrain.ReplaceContents(BlockIndex), data);
            result.CellFace = raycastResult.CellFace;
            return result;
        }

        public static int GetFrontFace(int value) => Terrain.ExtractData(value) switch {
            0 => 0,
            1 => 1,
            2 => 2,
            _ => 3
        };
    }
}
