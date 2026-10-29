using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Net;

namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// Game component that provides gamer services functionality.
    /// </summary>
    public class GamerServicesComponent : IGameComponent
    {
        /// <summary>
        /// Initializes a new GamerServicesComponent.
        /// </summary>
        /// <param name="game">The game instance.</param>
        public GamerServicesComponent(Game game)
        {
            this.game = game;
        }

        private readonly Game? game;

        /// <summary>
        /// Updates the gamer services.
        /// </summary>
        /// <param name="gameTime">Game timing information.</param>
        public void Update(GameTime gameTime)
        {
            GamerServicesDispatcher.Update();
        }

        /// <summary>
        /// Initializes the component.
        /// </summary>
        public void Initialize()
        {
            if (game != null)
                GamerServicesDispatcher.Initialize(game.Services);
        }
    }
}