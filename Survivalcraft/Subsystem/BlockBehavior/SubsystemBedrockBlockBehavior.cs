using TemplatesDatabase;

namespace Game;

public class SubsystemBedrockBlockBehavior : SubsystemBlockBehavior, IDrawable {
    public override int[] HandledBlocks => [1];

    public int[] DrawOrders => [2];

    public override bool OnInteract(TerrainRaycastResult raycastResult, ComponentMiner componentMiner) {
        return base.OnInteract(raycastResult, componentMiner);
    }

    public void Draw(Camera camera, int drawOrder) {
    }

    public override void Load(ValuesDictionary valuesDictionary) {
        base.Load(valuesDictionary);
    }
}
