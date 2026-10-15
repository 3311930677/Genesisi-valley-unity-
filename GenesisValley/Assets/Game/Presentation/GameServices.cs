using GenesisValley.Simulation;
using UnityEngine;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// Tiny service locator for the presentation layer. The simulation never sees this; views read the world through
    /// it and send intent only through <see cref="GameBootstrap"/> / the command processor (GDD §9/§10).
    /// </summary>
    public static class GameServices
    {
        private static ArtCatalog _art;
        public static ArtCatalog Art { get => _art != null ? _art : ArtCatalog.Empty; set { _art = value; MaterialLibrary.Clear(); } }
        public static GameBootstrap Game;
        public static WorldPresenter Presenter;
        public static GodCameraController CameraRig;
        public static ToolController Tools;
        public static SelectionController Selection;
        public static AudioDirector Audio;
        public static DayNightCycle Sky;
        public static FxLibrary Fx;
        public static GameSettings Settings;

        public static WorldState World => Game != null ? Game.World : null;

        public static void Reset()
        {
            Game = null; Presenter = null; CameraRig = null; Tools = null; Selection = null; Audio = null; Sky = null; Fx = null;
        }
    }
}
