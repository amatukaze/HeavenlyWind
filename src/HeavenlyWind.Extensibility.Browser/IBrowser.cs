﻿using System;
using System.Threading.Tasks;

namespace Sakuno.KanColle.Amatsukaze.Extensibility.Browser
{
    public interface IBrowser
    {
        event Action<bool, bool, string> LoadCompleted;

        void GoBack();
        void GoForward();

        void Navigate(string rpUrl);

        void Refresh();

        void SetZoom(double rpPercentage);

        Task<string> TakeScreenshotAsync();
    }
}
