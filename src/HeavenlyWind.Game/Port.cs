﻿using Newtonsoft.Json.Linq;
using Sakuno.Collections;
using Sakuno.KanColle.Amatsukaze.Game.Models;
using Sakuno.KanColle.Amatsukaze.Game.Models.Battle;
using Sakuno.KanColle.Amatsukaze.Game.Models.Raw;
using Sakuno.KanColle.Amatsukaze.Game.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Sakuno.KanColle.Amatsukaze.Game
{
    public class Port : ModelBase
    {
        public Admiral Admiral { get; private set; }

        public Materials Materials { get; } = new Materials();

        public HashSet<int> ShipIDs { get; private set; }
        public IDTable<Ship> Ships { get; } = new IDTable<Ship>();

        BattleParticipantSnapshot[] r_EvacuatedShips;
        internal HashSet<int> EvacuatedShipIDs { get; } = new HashSet<int>();

        public FleetManager Fleets { get; } = new FleetManager();

        public IDTable<Equipment> Equipment { get; } = new IDTable<Equipment>();

        static Regex r_UnequippedEquipmentRegex = new Regex(@"(?<=api_slottype)\d+", RegexOptions.Compiled);
        public IDictionary<EquipmentType, Equipment[]> UnequippedEquipment { get; } = new SortedList<EquipmentType, Equipment[]>();

        public IDTable<RepairDock> RepairDocks { get; } = new IDTable<RepairDock>();
        public IDTable<ConstructionDock> ConstructionDocks { get; } = new IDTable<ConstructionDock>();

        public QuestManager Quests { get; } = new QuestManager();

        public AirBase AirBase { get; } = new AirBase();

        public IDictionary<int, int> Items { get; } = new HybridDictionary<int, int>();

        public event Action<IDictionary<EquipmentType, Equipment[]>> UnequippedEquipmentUpdated = delegate { };

        internal Port()
        {
            ApiService.Subscribe("api_get_member/ship2", r =>
            {
                UpdateShips(r.Json["api_data"].ToObject<RawShip[]>());
                Fleets.Update(r.Json["api_data_deck"].ToObject<RawFleet[]>());
            });
            ApiService.Subscribe("api_get_member/ship_deck", r =>
            {
                var rData = r.GetData<RawShipsAndFleets>();

                foreach (var rRawShip in rData.Ships)
                    Ships[rRawShip.ID].Update(rRawShip);

                foreach (var rRawFleet in rData.Fleets)
                    Fleets[rRawFleet.ID].Update(rRawFleet);

                OnPropertyChanged(nameof(Ships));
            });
            ApiService.Subscribe("api_get_member/ship3", r =>
            {
                var rData = r.GetData<RawShipsAndFleets>();
                foreach (var rShip in rData.Ships)
                    Ships[rShip.ID].Update(rShip);
                Fleets.Update(rData.Fleets);

                ProcessUnequippedEquipment(r.Json["api_data"]["api_slot_data"]);
            });

            ApiService.Subscribe("api_get_member/unsetslot", r => ProcessUnequippedEquipment(r.Json["api_data"]));
            ApiService.Subscribe("api_get_member/require_info", r => ProcessUnequippedEquipment(r.Json["api_data"]["api_unsetslot"]));

            ApiService.Subscribe("api_req_kaisou/slotset", r =>
            {
                var rawData = r.GetData<RawEquipmentSetup>();

                if (rawData?.Bauxite is int bauxite)
                    Materials.Bauxite = bauxite;
            });
            ApiService.Subscribe("api_req_kaisou/slot_exchange_index", r =>
            {
                var rawData = r.GetData<RawEquipmentExchange>();

                if (Ships.TryGetValue(int.Parse(r.Parameters["api_id"]), out var rShip))
                {
                    if (rawData.EquipmentIDs != null)
                        rShip.UpdateEquipmentIDs(rawData.EquipmentIDs);
                    else
                        rShip.Update(rawData.Ship);

                    rShip.OwnerFleet?.Update();
                }

                if (rawData.Bauxite is int bauxite)
                    Materials.Bauxite = bauxite;
            });

            ApiService.Subscribe("api_req_kaisou/open_exslot", r =>
            {
                Ship rShip;
                if (Ships.TryGetValue(int.Parse(r.Parameters["api_id"]), out rShip))
                    rShip.InstallReinforcementExpansion();
            });

            ApiService.Subscribe("api_req_kaisou/slot_deprive", r =>
            {
                var rData = r.GetData<RawEquipmentRelocationResult>();

                var rDestionationShip = Ships[rData.Ships.Destination.ID];
                var rOriginShip = Ships[rData.Ships.Origin.ID];

                rDestionationShip.Update(rData.Ships.Destination);
                rOriginShip.Update(rData.Ships.Origin);

                rDestionationShip.OwnerFleet?.Update();
                rOriginShip.OwnerFleet?.Update();

                if (rData.UnequippedEquipment != null)
                    UnequippedEquipment[(EquipmentType)rData.UnequippedEquipment.Type] = rData.UnequippedEquipment.IDs.Select(rpID => Equipment[rpID]).ToArray();

                if (rData.Bauxite is int bauxite)
                    Materials.Bauxite = bauxite;
            });

            ApiService.Subscribe("api_req_kaisou/powerup", r =>
            {
                var rShipID = int.Parse(r.Parameters["api_id"]);
                var rData = r.GetData<RawModernizationResult>();
                var shouldConsumeSlotItem = r.Parameters["api_slot_dest_flag"] == "1";

                var rConsumedShips = r.Parameters["api_id_items"].Split(',').Select(rpID => Ships[int.Parse(rpID)]).ToArray();
                var rConsumedEquipment = rConsumedShips.SelectMany(rpShip => rpShip.EquipedEquipment).ToArray();

                if (shouldConsumeSlotItem)
                    foreach (var rEquipment in rConsumedEquipment)
                        Equipment.Remove(rEquipment);
                foreach (var rShip in rConsumedShips)
                    Ships.Remove(rShip);

                if (shouldConsumeSlotItem)
                    RemoveEquipmentFromUnequippedList(rConsumedEquipment);

                UpdateShipsCore();
                OnPropertyChanged(nameof(Equipment));
                Fleets.Update(rData.Fleets);

                Ship rModernizedShip;
                if (Ships.TryGetValue(rShipID, out rModernizedShip))
                {
                    if (!rData.Success)
                        Logger.Write(LoggingLevel.Info, string.Format(StringResources.Instance.Main.Log_Modernization_Failure, rModernizedShip.Info.TranslatedName));
                    else
                    {
                        var rOriginal = rModernizedShip.RawData;
                        var rNow = rData.Ship;
                        var rInfo = rModernizedShip.Info;

                        var rFirepowerDiff = rNow.ModernizedStatus[0] - rOriginal.ModernizedStatus[0];
                        var rTorpedoDiff = rNow.ModernizedStatus[1] - rOriginal.ModernizedStatus[1];
                        var rAADiff = rNow.ModernizedStatus[2] - rOriginal.ModernizedStatus[2];
                        var rArmorDiff = rNow.ModernizedStatus[3] - rOriginal.ModernizedStatus[3];
                        var rLuckDiff = rNow.ModernizedStatus[4] - rOriginal.ModernizedStatus[4];
                        var hpDiff = rNow.ModernizedStatus[5] - rOriginal.ModernizedStatus[5];
                        var aswDiff = rNow.ModernizedStatus[6] - rOriginal.ModernizedStatus[6];

                        var rFirepowerRemain = rInfo.FirepowerMaximum - rInfo.FirepowerMinimum - rNow.ModernizedStatus[0];
                        var rTorpedoRemain = rInfo.TorpedoMaximum - rInfo.TorpedoMinimum - rNow.ModernizedStatus[1];
                        var rAARemain = rInfo.AAMaximum - rInfo.AAMinimum - rNow.ModernizedStatus[2];
                        var rArmorRemain = rInfo.ArmorMaximum - rInfo.ArmorMinimum - rNow.ModernizedStatus[3];
                        var rLuckRemain = rNow.Luck[1] - rNow.Luck[0];
                        var hpRemain = 2 - rNow.ModernizedStatus[5];
                        var aswRemain = 9 - rNow.ModernizedStatus[6];

                        var rMaximumStatuses = new List<string>(5);
                        var rNotMaximumStatuses = new List<string>(5);

                        var rUseText = !Preference.Instance.UI.UseGameIconsInModernizationMessage.Value;
                        var rShowStatusGrowth = Preference.Instance.UI.ShowStatusGrowthInModernizationMessage.Value;

                        if (rFirepowerDiff > 0)
                        {
                            var rHeader = rUseText ? StringResources.Instance.Main.Ship_ToolTip_Status_Firepower : "[icon]firepower[/icon]";

                            if (rFirepowerRemain > 0)
                                rNotMaximumStatuses.Add(rShowStatusGrowth ? $"{rHeader} +{rFirepowerDiff}" : $"{rHeader} {rFirepowerRemain}");
                            else
                                rMaximumStatuses.Add(rHeader + " MAX");
                        }

                        if (rTorpedoDiff > 0)
                        {
                            var rHeader = rUseText ? StringResources.Instance.Main.Ship_ToolTip_Status_Torpedo : "[icon]torpedo[/icon]";

                            if (rTorpedoRemain > 0)
                                rNotMaximumStatuses.Add(rShowStatusGrowth ? $"{rHeader} +{rTorpedoDiff}" : $"{rHeader} {rTorpedoRemain}");
                            else
                                rMaximumStatuses.Add(rHeader + " MAX");
                        }

                        if (rAADiff > 0)
                        {
                            var rHeader = rUseText ? StringResources.Instance.Main.Ship_ToolTip_Status_AA : "[icon]aa[/icon]";

                            if (rAARemain > 0)
                                rNotMaximumStatuses.Add(rShowStatusGrowth ? $"{rHeader} +{rAADiff}" : $"{rHeader} {rAARemain}");
                            else
                                rMaximumStatuses.Add(rHeader + " MAX");
                        }

                        if (rArmorDiff > 0)
                        {
                            var rHeader = rUseText ? StringResources.Instance.Main.Ship_ToolTip_Status_Armor : "[icon]armor[/icon]";

                            if (rArmorRemain > 0)
                                rNotMaximumStatuses.Add(rShowStatusGrowth ? $"{rHeader} +{rArmorDiff}" : $"{rHeader} {rArmorRemain}");
                            else
                                rMaximumStatuses.Add(rHeader + " MAX");
                        }

                        if (rLuckDiff > 0)
                        {
                            var rHeader = rUseText ? StringResources.Instance.Main.Ship_ToolTip_Status_Luck : "[icon]luck[/icon]";

                            if (rLuckRemain > 0)
                                rNotMaximumStatuses.Add(rShowStatusGrowth ? $"{rHeader} +{rLuckDiff}" : $"{rHeader} {rLuckRemain}");
                            else
                                rMaximumStatuses.Add(rHeader +" MAX");
                        }

                        if (hpDiff > 0)
                        {
                            var header = rUseText ? StringResources.Instance.Main.Ship_ToolTip_Status_HP : "[icon]hp[/icon]";

                            if (hpRemain > 0)
                                rNotMaximumStatuses.Add(rShowStatusGrowth ? $"{header} +{hpDiff}" : $"{header} {hpRemain}");
                            else
                                rMaximumStatuses.Add(header + " MAX");
                        }

                        if (aswDiff > 0)
                        {
                            var header = rUseText ? StringResources.Instance.Main.Ship_ToolTip_Status_ASW : "[icon]asw[/icon]";

                            if (aswRemain > 0)
                                rNotMaximumStatuses.Add(rShowStatusGrowth ? $"{header} +{aswDiff}" : $"{header} {aswRemain}");
                            else
                                rMaximumStatuses.Add(header + " MAX");
                        }

                        var rBuilder = new StringBuilder(128);
                        rBuilder.AppendFormat(StringResources.Instance.Main.Log_Modernization_Success, rModernizedShip.Info.TranslatedName);

                        rBuilder.Append(" (");

                        var rSeparator = !Preference.Instance.UI.UseGameIconsInModernizationMessage.Value ? StringResources.Instance.Main.Log_Modernization_Separator_Type1 : " ";

                        if (rMaximumStatuses.Count > 0)
                            rBuilder.Append(rMaximumStatuses.Join(rSeparator));

                        if (rMaximumStatuses.Count > 0 && rNotMaximumStatuses.Count > 0)
                            rBuilder.Append(StringResources.Instance.Main.Log_Modernization_Separator_Type2);

                        if (rNotMaximumStatuses.Count > 0)
                        {
                            if (!rShowStatusGrowth)
                                rBuilder.Append(StringResources.Instance.Main.Log_Modernization_Remainder);

                            rBuilder.Append(rNotMaximumStatuses.Join(rSeparator));
                        }

                        rBuilder.Append(')');

                        Logger.Write(LoggingLevel.Info, rBuilder.ToString());
                    }

                    rModernizedShip.Update(rData.Ship);
                }
            });

            ApiService.Subscribe("api_req_kousyou/createship", r =>
            {
                var fuelConsumption = int.Parse(r.Parameters["api_item1"]);
                var bulletConsumption = int.Parse(r.Parameters["api_item2"]);
                var steelConsumption = int.Parse(r.Parameters["api_item3"]);
                var bauxiteConsumption = int.Parse(r.Parameters["api_item4"]);
                var developmentMaterialConsumption = int.Parse(r.Parameters["api_item5"]);
                var useInstantConstruction = r.Parameters["api_highspeed"] == "1";

                Materials.Fuel -= fuelConsumption;
                Materials.Bullet -= bulletConsumption;
                Materials.Steel -= steelConsumption;
                Materials.Bauxite -= bauxiteConsumption;
                Materials.DevelopmentMaterial -= developmentMaterialConsumption;

                if (useInstantConstruction)
                {
                    if (fuelConsumption >= 1000 && bulletConsumption >= 1000 && steelConsumption >= 1000 & bauxiteConsumption >= 1000)
                        Materials.InstantConstruction--;
                    else
                        Materials.InstantConstruction -= 10;
                }

                ConstructionDocks[int.Parse(r.Parameters["api_kdock_id"])].IsConstructionStarted = true;
            });
            ApiService.Subscribe("api_req_kousyou/getship", r =>
            {
                var rData = r.GetData<RawConstructionResult>();

                UpdateConstructionDocks(rData.ConstructionDocks);
                AddEquipment(rData.Equipment);

                Ships.Add(new Ship(rData.Ship));
                UpdateShipsCore();
            });
            ApiService.Subscribe("api_req_kousyou/createship_speedchange", r =>
            {
                if (r.Parameters["api_highspeed"] == "1")
                {
                    var rDock = ConstructionDocks[int.Parse(r.Parameters["api_kdock_id"])];
                    if (!rDock.IsLargeShipConstruction.GetValueOrDefault())
                        Materials.InstantConstruction--;
                    else
                        Materials.InstantConstruction -= 10;

                    rDock.CompleteConstruction();
                }
            });

            ApiService.Subscribe("api_req_kousyou/createitem", r =>
            {
                var rData = r.GetData<RawEquipmentDevelopment>();
                if (!rData.Success)
                    return;

                foreach (var item in rData.UnequippedEquipment)
                    UnequippedEquipment[(EquipmentType)item.Type] = item.EquipmentIDs.Select(id => Equipment[id]).ToArray();
            });

            ApiService.Subscribe("api_req_kousyou/destroyship", r =>
            {
                Materials.Update(r.Json["api_data"]["api_material"].ToObject<int[]>());

                var scrapEquipment = r.Parameters["api_slot_dest_flag"] == "1";

                foreach (var shipId in r.Parameters["api_ship_id"].Split(',').Select(int.Parse))
                {
                    var rShip = Ships[shipId];

                    if (scrapEquipment)
                    {
                        foreach (var rEquipment in rShip.EquipedEquipment)
                            Equipment.Remove(rEquipment);
                        OnPropertyChanged(nameof(Equipment));

                        RemoveEquipmentFromUnequippedList(rShip.EquipedEquipment);
                    }

                    rShip.OwnerFleet?.Remove(rShip);
                    Ships.Remove(rShip);
                }

                UpdateShipsCore();
            });
            ApiService.Subscribe("api_req_kousyou/destroyitem2", r =>
            {
                var rEquipmentIDs = r.Parameters["api_slotitem_ids"].Split(',').Select(int.Parse);

                foreach (var rEquipmentID in rEquipmentIDs)
                    Equipment.Remove(rEquipmentID);
                OnPropertyChanged(nameof(Equipment));

                var rMaterials = r.Json["api_data"]["api_get_material"].ToObject<int[]>();
                Materials.Fuel += rMaterials[0];
                Materials.Bullet += rMaterials[1];
                Materials.Steel += rMaterials[2];
                Materials.Bauxite += rMaterials[3];
            });

            ApiService.Subscribe("api_req_kousyou/remodel_slotlist_detail", r =>
            {
                var rID = r.Parameters["api_slot_id"];
                var rEquipment = Equipment[int.Parse(rID)];

                Logger.Write(LoggingLevel.Info, string.Format(StringResources.Instance.Main.Log_EquipmentImprovement_Ready, rEquipment.Info.TranslatedName, rEquipment.LevelText, rID));
            });
            ApiService.Subscribe("api_req_kousyou/remodel_slot", r =>
            {
                var rData = (RawImprovementResult)r.Data;
                var rEquipment = Equipment[int.Parse(r.Parameters["api_slot_id"])];

                Materials.Update(rData.Materials);

                if (!rData.Success)
                    Logger.Write(LoggingLevel.Info, string.Format(StringResources.Instance.Main.Log_EquipmentImprovement_Failure, rEquipment.Info.TranslatedName, rEquipment.LevelText));
                else
                {
                    var rUpgraded = rEquipment.Info.ID != rData.ImprovedEquipment.EquipmentID;

                    rEquipment.Update(rData.ImprovedEquipment);

                    if (!rUpgraded)
                        Logger.Write(LoggingLevel.Info, string.Format(StringResources.Instance.Main.Log_EquipmentImprovement_Success, rEquipment.Info.TranslatedName, rEquipment.LevelText));
                    else
                        Logger.Write(LoggingLevel.Info, string.Format(StringResources.Instance.Main.Log_EquipmentImprovement_Upgrade, rEquipment.Info.TranslatedName, rEquipment.LevelText));
                }

                if (rData.ConsumedEquipmentID != null)
                {
                    var rConsumedEquipment = rData.ConsumedEquipmentID.Select(rpID => Equipment[rpID]).ToArray();

                    foreach (var rEquipmentID in rData.ConsumedEquipmentID)
                        Equipment.Remove(rEquipmentID);
                    OnPropertyChanged(nameof(Equipment));

                    RemoveEquipmentFromUnequippedList(rConsumedEquipment);
                }
            });

            ApiService.Subscribe("api_req_hokyu/charge", r =>
            {
                var rData = r.GetData<RawSupplyResult>();
                var rFleets = new HashSet<Fleet>();

                Materials.Update(rData.Materials);

                foreach (var rShipSupplyResult in rData.Ships)
                {
                    var rShip = Ships[rShipSupplyResult.ID];
                    rShip.Fuel.Current = rShipSupplyResult.Fuel;
                    rShip.Bullet.Current = rShipSupplyResult.Bullet;

                    if (rShip.OwnerFleet != null)
                        rFleets.Add(rShip.OwnerFleet);

                    var rPlanes = rShipSupplyResult.Planes;
                    for (var i = 0; i < rShip.Slots.Count; i++)
                    {
                        var rCount = rPlanes[i];
                        if (rCount > 0)
                            rShip.Slots[i].PlaneCount = rCount;
                    }

                    rShip.CombatAbility.Update();
                }

                foreach (var rFleet in rFleets)
                    rFleet.Update();
            });
            ApiService.Subscribe("api_req_air_corps/supply", r =>
            {
                var rData = r.GetData<RawAirForceSquadronResupplyResult>();

                Materials.Fuel = rData.Fuel;
                Materials.Bauxite = rData.Bauxite;
            });

            ApiService.Subscribe("api_get_member/ndock", r => UpdateRepairDocks(r.GetData<RawRepairDock[]>()));
            ApiService.Subscribe("api_req_nyukyo/start", r =>
            {
                var rShip = Ships[int.Parse(r.Parameters["api_ship_id"])];
                var rIsInstantRepair = r.Parameters["api_highspeed"] == "1";
                rShip.Repair(rIsInstantRepair);
                rShip.OwnerFleet?.Update();

                Materials.Fuel -= rShip.RepairFuelConsumption;
                Materials.Steel -= rShip.RepairSteelConsumption;
                if (rIsInstantRepair)
                    Materials.Bucket--;
            });
            ApiService.Subscribe("api_req_nyukyo/speedchange", r =>
            {
                Materials.Bucket--;
                RepairDocks[int.Parse(r.Parameters["api_ndock_id"])].CompleteRepair();
            });

            ApiService.Subscribe(new[] { "api_req_sortie/battleresult", "api_req_combined_battle/battleresult" }, r =>
            {
                var rData = (RawBattleResult)r.Data;
                if (!rData.HasEvacuatedShip)
                    return;

                var currentStage = BattleInfo.Current.CurrentStage;

                BattleParticipantSnapshot evacuatedShip;
                var evacuatedShipIndex = rData.EvacuatedShips.EvacuatedShipIndex[0] - 1;
                if (evacuatedShipIndex < 6 || currentStage.FriendEscort == null)
                    evacuatedShip = currentStage.FriendMain[evacuatedShipIndex];
                else
                    evacuatedShip = currentStage.FriendEscort[evacuatedShipIndex - 6];

                if (rData.EvacuatedShips.EscortShipIndex == null)
                    r_EvacuatedShips = new[] { evacuatedShip };
                else
                {
                    BattleParticipantSnapshot escortShip;
                    var escortShipIndex = rData.EvacuatedShips.EscortShipIndex[0] - 1;
                    if (escortShipIndex < 6)
                        escortShip = currentStage.FriendMain[escortShipIndex];
                    else
                        escortShip = currentStage.FriendEscort[escortShipIndex - 6];

                    r_EvacuatedShips = new[] { evacuatedShip, escortShip };
                }
            });
            ApiService.Subscribe(new[] { "api_req_sortie/goback_port", "api_req_combined_battle/goback_port" }, delegate
            {
                if (SortieInfo.Current == null || r_EvacuatedShips == null || r_EvacuatedShips.Length == 0)
                    return;

                foreach (var rEvacuatedShip in r_EvacuatedShips)
                {
                    rEvacuatedShip.Evacuate();

                    EvacuatedShipIDs.Add(((FriendShip)rEvacuatedShip.Participant).Ship.ID);
                }
            });
            ApiService.Subscribe("api_get_member/ship_deck", _ => r_EvacuatedShips = null);
            ApiService.Subscribe("api_port/port", _ => EvacuatedShipIDs.Clear());

            ApiService.Subscribe("api_req_member/updatedeckname", r =>
            {
                var rFleet = Fleets[int.Parse(r.Parameters["api_deck_id"])];
                rFleet.Name = r.Parameters["api_name"];
            });

            ApiService.Subscribe("api_req_mission/return_instruction", r =>
            {
                var rFleet = Fleets[int.Parse(r.Parameters["api_deck_id"])];
                rFleet.ExpeditionStatus.Update(r.GetData<RawExpeditionRecalling>().Expedition);
            });

            ApiService.Subscribe("api_req_map/start", delegate
            {
                var rAirForceGroups = SortieInfo.Current?.AirForceGroups;
                if (rAirForceGroups == null)
                    return;

                Materials.Fuel -= rAirForceGroups.Sum(r => r.LBASFuelConsumption);
                Materials.Bullet -= rAirForceGroups.Sum(r => r.LBASBulletConsumption);
            });

            ApiService.Subscribe("api_req_air_corps/set_plane", r =>
            {
                var rData = r.GetData<RawAirForceGroupOrganization>();
                if (!rData.Bauxite.HasValue)
                    return;

                Materials.Bauxite = rData.Bauxite.Value;
            });

            ApiService.Subscribe("api_req_hensei/lock", r => Ships[int.Parse(r.Parameters["api_ship_id"])].IsLocked = (bool)r.Json["api_data"]["api_locked"]);
            ApiService.Subscribe("api_req_kaisou/lock", r => Equipment[int.Parse(r.Parameters["api_slotitem_id"])].IsLocked = (bool)r.Json["api_data"]["api_locked"]);

            ApiService.Subscribe("api_get_member/useitem", r => UpdateItemCount((JArray)r.Json["api_data"]));
        }

        #region Update

        internal void UpdateAdmiral(RawBasic rpAdmiral)
        {
            if (Admiral != null)
                Admiral.Update(rpAdmiral);
            else
            {
                Admiral = new Admiral(rpAdmiral);
                OnPropertyChanged(nameof(Admiral));
            }
        }

        internal void UpdatePort(RawPort rpPort)
        {
            UpdateAdmiral(rpPort.Basic);
            Materials.Update(rpPort.Materials);

            UpdateShips(rpPort);
            UpdateRepairDocks(rpPort.RepairDocks);

            Fleets.Update(rpPort);
        }

        internal void UpdateShips(RawPort rpPort) => UpdateShips(rpPort.Ships);
        internal void UpdateShips(RawShip[] rpShips)
        {
            if (Ships.UpdateRawData(rpShips, r => new Ship(r), (rpData, rpRawData) => rpData.Update(rpRawData)))
                UpdateShipsCore();
        }

        internal void UpdateShipsCore()
        {
            ShipIDs = new HashSet<int>(Ships.Values.Select(r => r.Info.ID));
            OnPropertyChanged(nameof(Ships));
        }

        internal void UpdateEquipment(RawEquipment[] rpEquipment)
        {
            if (Equipment.UpdateRawData(rpEquipment, r => new Equipment(r), (rpData, rpRawData) => rpData.Update(rpRawData)))
                OnPropertyChanged(nameof(Equipment));
        }
        internal void AddEquipment(Equipment rpEquipment)
        {
            Equipment.Add(rpEquipment);
            OnPropertyChanged(nameof(Equipment));
        }
        internal void AddEquipment(RawEquipment[] rpRawData)
        {
            if (rpRawData == null)
                return;

            foreach (var rRawData in rpRawData)
                Equipment.Add(new Equipment(rRawData));
            OnPropertyChanged(nameof(Equipment));
        }

        internal void UpdateConstructionDocks(RawConstructionDock[] rpConstructionDocks)
        {
            if (ConstructionDocks.UpdateRawData(rpConstructionDocks, r => new ConstructionDock(r), (rpData, rpRawData) => rpData.Update(rpRawData)))
                OnPropertyChanged(nameof(ConstructionDocks));
        }
        void UpdateRepairDocks(RawRepairDock[] rpDocks)
        {
            if (RepairDocks.UpdateRawData(rpDocks, r => new RepairDock(r), (rpData, rpRawData) => rpData.Update(rpRawData)))
                OnPropertyChanged(nameof(RepairDocks));
        }

        internal void UpdateUnequippedEquipment() => UnequippedEquipmentUpdated(UnequippedEquipment);

        #endregion

        void ProcessUnequippedEquipment(JToken rpJson)
        {
            foreach (var rEquipmentType in rpJson.Select(r => (JProperty)r))
            {
                var rMatch = r_UnequippedEquipmentRegex.Match(rEquipmentType.Name);
                if (!rMatch.Success)
                    continue;

                var rType = (EquipmentType)int.Parse(rMatch.Value);
                if (rEquipmentType.Value.Type != JTokenType.Array)
                    UnequippedEquipment[rType] = null;
                else
                    UnequippedEquipment[rType] = rEquipmentType.Value.Select(r =>
                    {
                        var rID = (int)r;

                        Equipment rEquipment;
                        if (!Equipment.TryGetValue(rID, out rEquipment))
                            AddEquipment(rEquipment = new Equipment(new RawEquipment() { ID = rID, EquipmentID = -1 }));

                        return rEquipment;
                    }).ToArray();
            }

            UpdateUnequippedEquipment();
        }
        void RemoveEquipmentFromUnequippedList(Equipment rpEquipment)
        {
            UnequippedEquipment[rpEquipment.Info.Type] = UnequippedEquipment[rpEquipment.Info.Type].Where(r => r != rpEquipment).ToArray();
            UpdateUnequippedEquipment();
        }
        void RemoveEquipmentFromUnequippedList(IEnumerable<Equipment> rpEquipment)
        {
            foreach (var rEquipment in rpEquipment)
                UnequippedEquipment[rEquipment.Info.Type] = UnequippedEquipment[rEquipment.Info.Type].Where(r => r != rEquipment).ToArray();
            UpdateUnequippedEquipment();
        }

        internal void UpdateItemCount(JArray rpItems)
        {
            if (rpItems == null)
                return;

            foreach (var rItem in rpItems)
            {
                var rID = (int)rItem["api_id"];
                var rCount = (int)rItem["api_count"];

                Items[rID] = rCount;
            }
        }
    }
}
