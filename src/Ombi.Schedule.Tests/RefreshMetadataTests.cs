using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MockQueryable.Moq;
using Moq;
using Moq.AutoMock;
using NUnit.Framework;
using Ombi.Api.External.ExternalApis.TheMovieDb;
using Ombi.Api.External.ExternalApis.TheMovieDb.Models;
using Ombi.Core.Settings;
using Ombi.Helpers;
using Ombi.Hubs;
using Ombi.Schedule.Jobs.Ombi;
using Ombi.Core.Settings.Models.External;
using Ombi.Store.Entities;
using Ombi.Store.Repository;
using Quartz;

namespace Ombi.Schedule.Tests
{
    [TestFixture]
    public class RefreshMetadataTests
    {
        private AutoMocker _mocker;
        private RefreshMetadata _subject;

        [SetUp]
        public void Setup()
        {
            _mocker = new AutoMocker();
            _subject = _mocker.CreateInstance<RefreshMetadata>();

            // RefreshMetadata.Execute triggers availability jobs when a
            // section runs; install the mock scheduler the suite already
            // uses so those triggers are no-ops.
            var scheduler = new Mock<IScheduler>();
            scheduler.Setup(x => x.GetCurrentlyExecutingJobs(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<IJobExecutionContext>());
            scheduler.Setup(x => x.TriggerJob(It.IsAny<JobKey>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _ = new QuartzMock(scheduler);

            _mocker.GetMock<INotificationHubService>()
                .Setup(x => x.SendNotificationToAdmins(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _mocker.GetMock<IMediaCacheService>()
                .Setup(x => x.Purge())
                .Returns(Task.CompletedTask);
            _mocker.Setup<ISettingsService<PlexSettings>, Task<PlexSettings>>(x => x.GetSettingsAsync())
                .ReturnsAsync(new PlexSettings { Enable = false, Servers = new List<PlexServers>() });
            _mocker.Setup<ISettingsService<EmbySettings>, Task<EmbySettings>>(x => x.GetSettingsAsync())
                .ReturnsAsync(new EmbySettings { Enable = false });
            _mocker.Setup<ISettingsService<JellyfinSettings>, Task<JellyfinSettings>>(x => x.GetSettingsAsync())
                .ReturnsAsync(new JellyfinSettings { Enable = false });
        }

        [Test]
        public async Task Execute_OneBadPlexMovie_ShouldNotAbortThePass()
        {
            _mocker.Setup<ISettingsService<PlexSettings>, Task<PlexSettings>>(x => x.GetSettingsAsync())
                .ReturnsAsync(new PlexSettings { Enable = true, Servers = new List<PlexServers>() });
            var plexRepo = _mocker.GetMock<IPlexContentRepository>();
            plexRepo.Setup(x => x.GetAll()).Returns(new List<PlexServerContent>
            {
                new PlexServerContent
                {
                    Title = "Bad",
                    Type = MediaType.Movie,
                    RequestId = null,
                    TheMovieDbId = "123",
                    ImdbId = null,
                },
            }.AsQueryable().BuildMock());
            _mocker.GetMock<IMovieDbApi>()
                .Setup(x => x.GetMovieInformationWithExtraInfo(It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync(new MovieResponseDto { ImdbId = "tt1" });
            plexRepo.Setup(x => x.UpdateWithoutSave(It.Is<PlexServerContent>(m => m.Title == "Bad")))
                .Throws(new System.Exception("boom"));
            plexRepo.Setup(x => x.SaveChangesAsync()).ReturnsAsync(1);

            await _subject.Execute(null);

            Assert.Multiple(() =>
            {
                // The final save ran, the job completed, and it reported
                // success: without per-item isolation the throw would have
                // aborted the pass at the job-level catch instead.
                plexRepo.Verify(x => x.SaveChangesAsync(), Times.AtLeastOnce);
                _mocker.GetMock<IMediaCacheService>().Verify(x => x.Purge(), Times.Once);
                _mocker.GetMock<INotificationHubService>().Verify(
                    x => x.SendNotificationToAdmins("Metadata Refresh Finished", It.IsAny<CancellationToken>()),
                    Times.Once);
                _mocker.GetMock<INotificationHubService>().Verify(
                    x => x.SendNotificationToAdmins("Metadata Refresh Failed", It.IsAny<CancellationToken>()),
                    Times.Never);
            });
        }

        [Test]
        public async Task Execute_ManyEmbyMovies_ShouldSavePeriodically()
        {
            _mocker.Setup<ISettingsService<EmbySettings>, Task<EmbySettings>>(x => x.GetSettingsAsync())
                .ReturnsAsync(new EmbySettings { Enable = true });
            var embyRepo = _mocker.GetMock<IEmbyContentRepository>();
            var movies = Enumerable.Range(0, 101).Select(i => new EmbyContent
            {
                Title = "Movie " + i,
                Type = MediaType.Movie,
                TheMovieDbId = null,
                ImdbId = null,
            }).ToList();
            embyRepo.Setup(x => x.GetAll()).Returns(movies.AsQueryable().BuildMock());
            embyRepo.Setup(x => x.SaveChangesAsync()).ReturnsAsync(1);

            await _subject.Execute(null);

            // One periodic save at 100 items, the movies' final save, and
            // the TV pass's final save (its query finds no series, so it
            // saves nothing). The movies need no lookups (no ids, no
            // servers to recheck against), so every item reaches the
            // periodic check.
            embyRepo.Verify(x => x.SaveChangesAsync(), Times.Exactly(3));
        }
    }
}
