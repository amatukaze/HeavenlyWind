﻿using Sakuno.ING.Game.Models;

namespace Sakuno.ING.Game.Events
{
    public sealed record SlotItemsDeveloped(bool IsSuccessful, RawSlotItem?[] SlotItems)
    {
    }
}
