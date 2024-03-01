﻿using Sakuno.KanColle.Amatsukaze.Game.Models.Battle.Phases;
using Sakuno.KanColle.Amatsukaze.Game.Models.Raw.Battle;
using Sakuno.KanColle.Amatsukaze.Game.Parsers;
using System.Collections.Generic;

namespace Sakuno.KanColle.Amatsukaze.Game.Models.Battle.Stages
{
    class DayNormalStage : Day
    {
        public override BattleStageType Type => BattleStageType.Day;

        public override IList<BattlePhase> Phases => new BattlePhase[]
        {
            LandBaseJetAircraftAerialSupport,
            JetAircraftAerialCombat,
            LandBaseAerialSupport,
            AerialCombat,
            SupportingFire,
            OpeningASW,
            OpeningTorpedo,

            ShellingFirstRound,
            ShellingSecondRound,

            ClosingTorpedo,
        };

        internal protected DayNormalStage(BattleInfo rpOwner, ApiInfo rpInfo) : base(rpOwner)
        {
            var rRawData = rpInfo.Data as RawDay;

            LandBaseJetAircraftAerialSupport = new LandBaseJetAircraftAerialSupport(this, rRawData.LandBaseJetAircraftAerialSupport);
            JetAircraftAerialCombat = new AerialCombatPhase(this, rRawData.JetAircraftAerialCombat);
            LandBaseAerialSupport = new LandBaseAerialSupportPhase(this, rRawData.LandBaseAerialSupport);
            AerialCombat = new AerialCombatPhase(this, rRawData.AerialCombat);
            SupportingFire = new SupportingFirePhase(this, rRawData.SupportingFire);
            OpeningASW = new ShellingPhase(this, rRawData.OpeningASW);
            OpeningTorpedo = new OpeningTorpedoSalvoPhase(this, rRawData.OpeningTorpedoSalvo);

            ShellingFirstRound = new ShellingPhase(this, rRawData.ShellingFirstRound);
            ShellingSecondRound = new ShellingPhase(this, rRawData.ShellingSecondRound);

            ClosingTorpedo = new TorpedoSalvoPhase(this, rRawData.ClosingTorpedoSalvo);
        }
    }
}
