using System;
using System.Collections.Generic;
using System.Linq;

using Server.Commands;
using Server.Items;
using Server.Mobiles;
using Server.Regions;

namespace Server.Engines.Doom
{
    public enum GauntletSpawnerState
    {
        InSequence,
        InProgress,
        Completed
    }

    public class GauntletSpawner : Item
    {
        public const int PlayersPerSpawn = 5;
        public const int InSequenceItemHue = 0x000;
        public const int InProgressItemHue = 0x676;
        public const int CompletedItemHue = 0x455;

        private GauntletSpawnerState m_State;

        private string m_TypeName;

        private BaseDoor m_Door;
        private BaseAddon m_Addon;

        private GauntletSpawner m_Sequence;

        private List<Mobile> m_Creatures;
        private List<BaseTrap> m_Traps;

        private Rectangle2D m_RegionBounds;

        private Region m_Region;
        private Timer m_Timer;

        [Constructable]
        public GauntletSpawner()
            : this(null)
        {
        }

        [Constructable]
        public GauntletSpawner(string typeName)
            : base(0x36FE)
        {
            this.Visible = false;
            this.Movable = false;

            this.m_TypeName = typeName;
            this.m_Creatures = new List<Mobile>();
            this.m_Traps = new List<BaseTrap>();
        }

        public GauntletSpawner(Serial serial)
            : base(serial)
        {
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public string TypeName
        {
            get
            {
                return this.m_TypeName;
            }
            set
            {
                this.m_TypeName = value;
            }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public BaseDoor Door
        {
            get
            {
                return this.m_Door;
            }
            set
            {
                this.m_Door = value;
            }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public BaseAddon Addon
        {
            get
            {
                return this.m_Addon;
            }
            set
            {
                this.m_Addon = value;
            }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public GauntletSpawner Sequence
        {
            get
            {
                return this.m_Sequence;
            }
            set
            {
                this.m_Sequence = value;
            }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool HasCompleted
        {
            get
            {
                if (this.m_Creatures.Count == 0)
                    return false;

                for (int i = 0; i < this.m_Creatures.Count; ++i)
                {
                    Mobile mob = this.m_Creatures[i];

                    if (!mob.Deleted)
                        return false;
                }

                return true;
            }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public Rectangle2D RegionBounds
        {
            get
            {
                return this.m_RegionBounds;
            }
            set
            {
                this.m_RegionBounds = value;
            }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public GauntletSpawnerState State
        {
            get
            {
                return this.m_State;
            }
            set
            {
                if (this.m_State == value)
                    return;

                this.m_State = value;

                int hue = 0;
                bool lockDoors = (this.m_State == GauntletSpawnerState.InProgress);

                switch ( this.m_State )
                {
                    case GauntletSpawnerState.InSequence:
                        hue = InSequenceItemHue;
                        break;
                    case GauntletSpawnerState.InProgress:
                        hue = InProgressItemHue;
                        break;
                    case GauntletSpawnerState.Completed:
                        hue = CompletedItemHue;
                        break;
                }

                if (this.m_Door != null)
                {
                    this.m_Door.Hue = hue;
                    this.m_Door.Locked = lockDoors;

                    if (lockDoors)
                    {
                        this.m_Door.KeyValue = Key.RandomValue();
                        this.m_Door.Open = false;
                    }

                    if (this.m_Door.Link != null)
                    {
                        this.m_Door.Link.Hue = hue;
                        this.m_Door.Link.Locked = lockDoors;

                        if (lockDoors)
                        {
                            this.m_Door.Link.KeyValue = Key.RandomValue();
                            this.m_Door.Open = false;
                        }
                    }
                }

                if (this.m_Addon != null)
                    this.m_Addon.Hue = hue;

                if (this.m_State == GauntletSpawnerState.InProgress)
                {
                    this.CreateRegion();
                    this.FullSpawn();

                    this.m_Timer = Timer.DelayCall(TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.0), new TimerCallback(Slice));
                }
                else
                {
                    this.ClearCreatures();
                    this.ClearTraps();
                    this.DestroyRegion();

                    if (this.m_Timer != null)
                        this.m_Timer.Stop();

                    this.m_Timer = null;
                }
            }
        }

        public List<Mobile> Creatures
        {
            get
            {
                return this.m_Creatures;
            }
            set
            {
                this.m_Creatures = value;
            }
        }

        public List<BaseTrap> Traps
        {
            get
            {
                return this.m_Traps;
            }
            set
            {
                this.m_Traps = value;
            }
        }

        public Region Region
        {
            get
            {
                return this.m_Region;
            }
            set
            {
                this.m_Region = value;
            }
        }

        public override string DefaultName
        {
            get
            {
                return "doom spawner";
            }
        }

        public static void Initialize()
        {
            CommandSystem.Register("GenGauntlet", AccessLevel.Administrator, new CommandEventHandler(GenGauntlet_OnCommand));
			CommandSystem.Register("DeleteGauntlet", AccessLevel.Administrator, new CommandEventHandler(DeleteGauntlet_OnCommand));
		}

		private static bool FindObject<T>(Map map, Point3D p, int range, out T value) where T : IEntity
		{
			return FindObject(map, p, range, null, out value);
		}

		private static bool FindObject<T>(Map map, Point3D p, int range, Func<T, bool> predicate, out T value)
			where T : IEntity
		{
			value = default(T);

			var any = false;

			var e = map.GetObjectsInRange(p, range);

			foreach (var o in (predicate != null ? e.OfType<T>().Where(predicate) : e.OfType<T>()).Where(o => o != null))
			{
				value = o;
				any = true;
				break;
			}

			e.Free();

			return any;
		}

		private static void DeleteDoor(BaseDoor d)
		{
			if (d == null)
			{
				return;
			}

			if (d.Link != null)
			{
				DeleteDoor(d.Link);
			}

			d.Delete();
		}

	    public static void CreateTeleporter(int xFrom, int yFrom, int xTo, int yTo)
	    {
		    var p = new Point3D(xFrom, yFrom, -1);

		    Static telePad;

		    if (FindObject(Map.Malas, p, 0, out telePad))
		    {
			    telePad.Delete();
		    }

		    telePad = new Static(0x1822);
		    telePad.MoveToWorld(p, Map.Malas);
		    telePad.Hue = 0x482;

			WeakEntityCollection.Add("doom", telePad);

		    Teleporter teleItem;

		    if (FindObject(Map.Malas, p, 0, out teleItem))
		    {
			    teleItem.Delete();
		    }

			teleItem = new Teleporter(new Point3D(xTo, yTo, -1), Map.Malas, false);
		    teleItem.MoveToWorld(p, Map.Malas);
		    teleItem.SourceEffect = true;
		    teleItem.DestEffect = true;
		    teleItem.SoundID = 0x1FE;

			WeakEntityCollection.Add("doom", teleItem);
	    }

	    public static BaseDoor CreateDoorSet(int xDoor, int yDoor, bool doorEastToWest, int hue)
	    {
		    var ph = new Point3D(xDoor, yDoor, -1);
		    var pl = new Point3D(xDoor + (doorEastToWest ? 0 : 1), yDoor + (doorEastToWest ? 1 : 0), -1);

		    BaseDoor hiDoor, loDoor;

		    if (FindObject(Map.Malas, ph, 0, out hiDoor))
		    {
			    DeleteDoor(hiDoor);
		    }

			if (FindObject(Map.Malas, pl, 0, out loDoor))
			{
				DeleteDoor(loDoor);
			}

		    hiDoor = new MetalDoor(doorEastToWest ? DoorFacing.NorthCCW : DoorFacing.WestCW);
			loDoor = new MetalDoor(doorEastToWest ? DoorFacing.SouthCW : DoorFacing.EastCCW);

            hiDoor.MoveToWorld(ph, Map.Malas);
            loDoor.MoveToWorld(pl, Map.Malas);

            hiDoor.Link = loDoor;
            loDoor.Link = hiDoor;

            hiDoor.Hue = hue;
            loDoor.Hue = hue;

			WeakEntityCollection.Add("doom", hiDoor);
			WeakEntityCollection.Add("doom", loDoor);

            return hiDoor;
		}

	    public static GauntletSpawner CreateSpawner(
		    string typeName,
			    int xSpawner,
			    int ySpawner,
			    int xDoor,
			    int yDoor,
			    int xPentagram,
			    int yPentagram,
			    bool doorEastToWest,
			    int xStart,
			    int yStart,
			    int xWidth,
			    int yHeight)
	    {
		    var sp = new Point3D(xSpawner, ySpawner, -1);

		    GauntletSpawner spawner;

		    if (FindObject(Map.Malas, sp, 0, out spawner))
		    {
			    spawner.Delete();
		    }

		    spawner = new GauntletSpawner(typeName);
			spawner.MoveToWorld(sp, Map.Malas);

		    if (spawner.Door != null)
		    {
			    DeleteDoor(spawner.Door);
		    }

		    if (xDoor > 0 && yDoor > 0)
		    {
			    spawner.Door = CreateDoorSet(xDoor, yDoor, doorEastToWest, 0);
		    }

		    spawner.RegionBounds = new Rectangle2D(xStart, yStart, xWidth, yHeight);

		    if (spawner.Addon != null)
		    {
			    spawner.Addon.Delete();
			    spawner.Addon = null;
		    }

		    if (xPentagram > 0 && yPentagram > 0)
		    {
			    var pp = new Point3D(xPentagram, yPentagram, -1);

			    PentagramAddon pentagram;

			    if (FindObject(Map.Malas, pp, 0, out pentagram))
			    {
				    pentagram.Delete();
			    }

			    pentagram = new PentagramAddon();
			    pentagram.MoveToWorld(pp, Map.Malas);

				WeakEntityCollection.Add("doom", pentagram);

			    spawner.Addon = pentagram;
		    }

			WeakEntityCollection.Add("doom", spawner);

		    return spawner;
	    }

	    public static void CreatePricedHealer(int price, int x, int y)
	    {
		    var p = new Point3D(x, y, -1);

		    PricedHealer healer;

		    if (FindObject(Map.Malas, p, 10, out healer))
		    {
			    healer.Delete();
		    }
		    
			healer = new PricedHealer(price);
		    healer.MoveToWorld(p, Map.Malas);
		    healer.Home = p;
		    healer.RangeHome = 5;

			WeakEntityCollection.Add("doom", healer);
	    }

	    public static void CreateMorphItem(int x, int y, int inactiveItemID, int activeItemID, int range, int hue)
	    {
		    var p = new Point3D(x, y, -1);

		    MorphItem item;

		    if (FindObject(Map.Malas, p, 0, out item))
			{
				item.Delete();
		    }

		    item = new MorphItem(inactiveItemID, activeItemID, range);
			item.MoveToWorld(p, Map.Malas);
			item.Hue = hue;

			WeakEntityCollection.Add("doom", item);
        }

        public static void CreateVarietyDealer(int x, int y)
        {
	        var p = new Point3D(x, y, -1);

            VarietyDealer dealer;

	        if (FindObject(Map.Malas, p, 10, out dealer))
	        {
		        dealer.Delete();
	        }

	        dealer = new VarietyDealer
	        {
		        Name = "Nix",
		        Title = "the Variety Dealer",
		        Body = 400,
		        Female = false,
		        Hue = 0x8835
	        };

			WeakEntityCollection.Add("doom", dealer);

	        /* Begin outfit */

	        var ic = dealer.Items.Count;

	        while (--ic >= 0)
	        {
		        if (ic >= dealer.Items.Count)
		        {
					continue;
		        }

		        var item = dealer.Items[ic];
				
                if (item.Layer != Layer.ShopBuy && item.Layer != Layer.ShopResale && item.Layer != Layer.ShopSell)
                    item.Delete();
            }

            dealer.HairItemID = 0x2049; // Pig Tails
            dealer.HairHue = 0x482;

            dealer.FacialHairItemID = 0x203E;
            dealer.FacialHairHue = 0x482;

            dealer.AddItem(new FloppyHat(1));
            dealer.AddItem(new Robe(1));

            dealer.AddItem(new LanternOfSouls());

            dealer.AddItem(new Sandals(0x482));
            /* End outfit */

            dealer.MoveToWorld(p, Map.Malas);

            dealer.Home = p;
            dealer.RangeHome = 2;
        }

		public static void DeleteGauntlet_OnCommand(CommandEventArgs e)
		{
			WeakEntityCollection.Delete("doom");
		}

        public static void GenGauntlet_OnCommand(CommandEventArgs e)
		{
			WeakEntityCollection.Delete("doom");

            /* Begin healer room */
            CreatePricedHealer(5000, 387, 400);
            CreateTeleporter(390, 407, 394, 405);

            BaseDoor healerDoor = CreateDoorSet(393, 404, true, 0x44E);

            healerDoor.Locked = true;
            healerDoor.KeyValue = Key.RandomValue();

            if (healerDoor.Link != null)
            {
                healerDoor.Link.Locked = true;
                healerDoor.Link.KeyValue = Key.RandomValue();
            }
            /* End healer room */

            /* Begin supply room */
            CreateMorphItem(433, 371, 0x29F, 0x116, 3, 0x44E);
            CreateMorphItem(433, 372, 0x29F, 0x115, 3, 0x44E);

            CreateVarietyDealer(492, 369);

	        foreach (var s in Map.Malas.GetItemsInBounds(new Rectangle2D(434, 371, 44, 1)))
	        {
		        s.Delete();
	        }

	        for (int x = 434; x <= 478; ++x)
            {
	            for (int y = 371; y <= 372; ++y)
	            {
		            var item = new Static(0x524)
		            {
			            Hue = 1
					};

					item.MoveToWorld(new Point3D(x, y, -1), Map.Malas);

					WeakEntityCollection.Add("doom", item);
	            }
            }
            /* End supply room */

            /* Begin gauntlet cycle */
            CreateTeleporter(471, 428, 474, 428);
            CreateTeleporter(462, 494, 462, 498);
            CreateTeleporter(403, 502, 399, 506);
            CreateTeleporter(357, 476, 356, 480);
            CreateTeleporter(361, 433, 357, 434);

            GauntletSpawner sp1 = CreateSpawner("DarknightCreeper", 491, 456, 473, 432, 417, 426, true, 473, 412, 39, 60);
            GauntletSpawner sp2 = CreateSpawner("FleshRenderer", 482, 520, 468, 496, 426, 422, false, 448, 496, 56, 48);
            GauntletSpawner sp3 = CreateSpawner("Impaler", 406, 538, 408, 504, 432, 430, false, 376, 504, 64, 48);
            GauntletSpawner sp4 = CreateSpawner("ShadowKnight", 335, 512, 360, 478, 424, 439, false, 300, 478, 72, 64);
            GauntletSpawner sp5 = CreateSpawner("AbysmalHorror", 326, 433, 360, 429, 416, 435, true, 300, 408, 60, 56);
            GauntletSpawner sp6 = CreateSpawner("DemonKnight", 423, 430, 0, 0, 423, 430, true, 392, 392, 72, 96);

            sp1.Sequence = sp2;
            sp2.Sequence = sp3;
            sp3.Sequence = sp4;
            sp4.Sequence = sp5;
            sp5.Sequence = sp6;
            sp6.Sequence = sp1;

            sp1.State = GauntletSpawnerState.InProgress;
            /* End gauntlet cycle */

            /* Begin exit gate */
	        var p = new Point3D(433, 326, 4);

            ConfirmationMoongate gate;

	        if (FindObject(Map.Malas, p, 0, out gate))
	        {
		        gate.Delete();
	        }

	        gate = new ConfirmationMoongate
	        {
		        Dispellable = false,
		        Target = new Point3D(2350, 1270, -85),
		        TargetMap = Map.Malas,
		        GumpWidth = 420,
		        GumpHeight = 280,
		        MessageColor = 0x7F00,
		        MessageNumber = 1062109, // You are about to exit Dungeon Doom.  Do you wish to continue?
		        TitleColor = 0x7800,
		        TitleNumber = 1062108, // Please verify...
		        Hue = 0x44E
	        };

	        gate.MoveToWorld(p, Map.Malas);

			WeakEntityCollection.Add("doom", gate);
            /* End exit gate */
        }

        public virtual void CreateRegion()
        {
            if (this.m_Region != null)
                return;

            Map map = this.Map;

            if (map == null || map == Map.Internal)
                return;

            this.m_Region = new GauntletRegion(this, map);
        }

        public virtual void DestroyRegion()
        {
            if (this.m_Region != null)
                this.m_Region.Unregister();

            this.m_Region = null;
        }

        public virtual int ComputeTrapCount()
        {
            int area = this.m_RegionBounds.Width * this.m_RegionBounds.Height;

            return area / 100;
        }

        public virtual void ClearTraps()
        {
            for (int i = 0; i < this.m_Traps.Count; ++i)
                this.m_Traps[i].Delete();

            this.m_Traps.Clear();
        }

        public virtual void SpawnTrap()
        {
            Map map = this.Map;

            if (map == null)
                return;

            BaseTrap trap = null;

            int random = Utility.Random(100);

            if (22 > random)
                trap = new SawTrap(Utility.RandomBool() ? SawTrapType.WestFloor : SawTrapType.NorthFloor);
            else if (44 > random)
                trap = new SpikeTrap(Utility.RandomBool() ? SpikeTrapType.WestFloor : SpikeTrapType.NorthFloor);
            else if (66 > random)
                trap = new GasTrap(Utility.RandomBool() ? GasTrapType.NorthWall : GasTrapType.WestWall);
            else if (88 > random)
                trap = new FireColumnTrap();
            else
                trap = new MushroomTrap();

            if (trap == null)
                return;

            if (trap is FireColumnTrap || trap is MushroomTrap)
                trap.Hue = 0x451;

            // try 10 times to find a valid location
            for (int i = 0; i < 10; ++i)
            {
                int x = Utility.Random(this.m_RegionBounds.X, this.m_RegionBounds.Width);
                int y = Utility.Random(this.m_RegionBounds.Y, this.m_RegionBounds.Height);
                int z = this.Z;

                if (!map.CanFit(x, y, z, 16, false, false))
                    z = map.GetAverageZ(x, y);

                if (!map.CanFit(x, y, z, 16, false, false))
                    continue;

                trap.MoveToWorld(new Point3D(x, y, z), map);
                this.m_Traps.Add(trap);

                return;
            }

            trap.Delete();
        }

        public virtual int ComputeSpawnCount()
        {
            int playerCount = 0;

            Map map = this.Map;

            if (map != null)
            {
                Point3D loc = this.GetWorldLocation();

                Region reg = Region.Find(loc, map).GetRegion("Doom Gauntlet");

                if (reg != null)
                    playerCount = reg.GetPlayerCount();
            }

            if (playerCount == 0 && this.m_Region != null)
                playerCount = this.m_Region.GetPlayerCount();

            int count = (playerCount + PlayersPerSpawn - 1) / PlayersPerSpawn;

            if (count < 1)
                count = 1;

            return count;
        }

        public virtual void ClearCreatures()
        {
            for (int i = 0; i < this.m_Creatures.Count; ++i)
                this.m_Creatures[i].Delete();

            this.m_Creatures.Clear();
        }

        public virtual void FullSpawn()
        {
            this.ClearCreatures();

            int count = this.ComputeSpawnCount();

            for (int i = 0; i < count; ++i)
                this.Spawn();

            this.ClearTraps();

            count = this.ComputeTrapCount();

            for (int i = 0; i < count; ++i)
                this.SpawnTrap();
        }

        public virtual void Spawn()
        {
            try
            {
                if (this.m_TypeName == null)
                    return;

                Type type = ScriptCompiler.FindTypeByName(this.m_TypeName, true);

                if (type == null)
                    return;

                object obj = Activator.CreateInstance(type);

                if (obj == null)
                    return;

                if (obj is Item)
                {
                    ((Item)obj).Delete();
                }
                else if (obj is Mobile)
                {
                    Mobile mob = (Mobile)obj;

                    mob.MoveToWorld(this.GetWorldLocation(), this.Map);

                    this.m_Creatures.Add(mob);
                }
            }
            catch
            {
            }
        }

        public virtual void RecurseReset()
        {
            if (this.m_State != GauntletSpawnerState.InSequence)
            {
                this.State = GauntletSpawnerState.InSequence;

                if (this.m_Sequence != null && !this.m_Sequence.Deleted)
                    this.m_Sequence.RecurseReset();
            }
        }

        public virtual void Slice()
        {
            if (this.m_State != GauntletSpawnerState.InProgress)
                return;

            int count = this.ComputeSpawnCount();

            for (int i = this.m_Creatures.Count; i < count; ++i)
                this.Spawn();

            if (this.HasCompleted)
            {
                this.State = GauntletSpawnerState.Completed;

                if (this.m_Sequence != null && !this.m_Sequence.Deleted)
                {
                    if (this.m_Sequence.State == GauntletSpawnerState.Completed)
                        this.RecurseReset();

                    this.m_Sequence.State = GauntletSpawnerState.InProgress;
                }
            }
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);

            writer.Write((int)1); // version

            writer.Write(this.m_RegionBounds);

            writer.WriteItemList<BaseTrap>(this.m_Traps, false);

            writer.Write(this.m_Creatures, false);

            writer.Write(this.m_TypeName);
            writer.WriteItem<BaseDoor>(this.m_Door);
            writer.WriteItem<BaseAddon>(this.m_Addon);
            writer.WriteItem<GauntletSpawner>(this.m_Sequence);

            writer.Write((int)this.m_State);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);

            int version = reader.ReadInt();

            switch ( version )
            {
                case 1:
                    {
                        this.m_RegionBounds = reader.ReadRect2D();
                        this.m_Traps = reader.ReadStrongItemList<BaseTrap>();

                        goto case 0;
                    }
                case 0:
                    {
                        if (version < 1)
                        {
                            this.m_Traps = new List<BaseTrap>();
                            this.m_RegionBounds = new Rectangle2D(this.X - 40, this.Y - 40, 80, 80);
                        }

                        this.m_Creatures = reader.ReadStrongMobileList();

                        this.m_TypeName = reader.ReadString();
                        this.m_Door = reader.ReadItem<BaseDoor>(); 
                        this.m_Addon = reader.ReadItem<BaseAddon>(); 
                        this.m_Sequence = reader.ReadItem<GauntletSpawner>();

                        this.State = (GauntletSpawnerState)reader.ReadInt();

                        break;
                    }
            }
        }
    }

    public class GauntletRegion : BaseRegion
    {
        private readonly GauntletSpawner m_Spawner;
        public GauntletRegion(GauntletSpawner spawner, Map map)
            : base(null, map, Region.Find(spawner.Location, spawner.Map), spawner.RegionBounds)
        {
            this.m_Spawner = spawner;

            this.GoLocation = spawner.Location;

            this.Register();
        }

        public override void AlterLightLevel(Mobile m, ref int global, ref int personal)
        {
            global = 12;
        }

        public override void OnEnter(Mobile m)
        {
        }

        public override void OnExit(Mobile m)
        {
        }
    }
}