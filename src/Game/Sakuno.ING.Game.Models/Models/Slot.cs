﻿using Sakuno.ING.Game.Models.MasterData;

namespace Sakuno.ING.Game.Models
{
    public partial class Slot : BindableObject
    {
        private EquipmentInfo _equipment;
        public EquipmentInfo Equipment
        {
            get => _equipment;
            internal set
            {
                if (_equipment != value)
                    using (EnterBatchNotifyScope())
                    {
                        bool isEmptyChanged = _equipment is null || value is null;
                        _equipment = value;
                        if (isEmptyChanged)
                            NotifyPropertyChanged(nameof(IsEmpty));
                        NotifyPropertyChanged();
                        UpdateCalculations();
                    }
            }
        }

        public int ImprovementLevel { get; protected set; }
        public int AirProficiency { get; protected set; }

        private ClampedValue _aircraft;
        public ClampedValue Aircraft
        {
            get => _aircraft;
            internal set
            {
                if (_aircraft != value)
                    using (EnterBatchNotifyScope())
                    {
                        _aircraft = value;
                        NotifyPropertyChanged();
                        UpdateCalculations();
                    }
            }
        }

        public bool IsEmpty => Equipment is null;

        private AirFightPower _airFightPower;
        public AirFightPower AirFightPower
        {
            get => _airFightPower;
            private set => Set(ref _airFightPower, value);
        }

        private double _effectiveLoS;
        public double EffectiveLoS
        {
            get => _effectiveLoS;
            private set => Set(ref _effectiveLoS, value);
        }

        protected Slot() { }
        public Slot(EquipmentInfo equipment, ClampedValue aircraft = default, int improvementLevel = 0, int airProficiency = 0)
        {
            Equipment = equipment;
            Aircraft = aircraft;
            ImprovementLevel = improvementLevel;
            AirProficiency = airProficiency;
            UpdateCalculations();
        }
    }
}
