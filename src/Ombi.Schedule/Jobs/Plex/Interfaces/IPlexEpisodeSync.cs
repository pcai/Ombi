using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ombi.Api.External.MediaServers.Plex.Models;
using Ombi.Store.Entities;

namespace Ombi.Schedule.Jobs.Plex.Interfaces
{
    public interface IPlexEpisodeSync : IBaseJob
    {
        /// <summary>
        /// Processes one batch of episodes, returning the ones that were
        /// added. When <paramref name="sharedKeys"/> is supplied, it is
        /// the key set shared across batches: an empty set is populated
        /// from <paramref name="currentEpisodes"/> on first use, and
        /// newly saved keys are added to it, so pass one set per sync
        /// run. When it is null, the keys are loaded once for the call.
        /// </summary>
        Task<HashSet<PlexEpisode>> ProcessEpsiodes(Metadata[] episodes, IQueryable<PlexEpisode> currentEpisodes, HashSet<(string Key, string GrandparentKey)> sharedKeys = null);
    }
}