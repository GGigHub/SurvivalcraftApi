var project = Game.GameManager.Project;
if (project == null) return "no-project";
var blockEntity = project.FindSubsystem<Game.SubsystemBlockEntities>().GetBlockEntity(@@0@@, @@1@@, @@2@@);
if (blockEntity == null) return "no-block-entity";
var tank = blockEntity.Entity.FindComponent<SCIENEW.ComponentFluidTank>();
if (tank == null) return "no-fluid-tank";
tank.AddTankFluid(0, Game.Terrain.MakeBlockValue(Game.WaterBlock.Index), @@3@@f, out var overflow);
return string.Format("value={0};volume={1:R};overflow={2:R}",
    tank.GetTankValue(0),
    tank.GetTankVolume(0),
    overflow);