using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MockQueryable.Moq;
using Moq;
using Moq.AutoMock;
using NUnit.Framework;
using Ombi.Api.External.MediaServers.Plex.Models;
using Ombi.Core.Settings.Models.External;
using Ombi.Schedule.Jobs.Plex;
using Ombi.Store.Entities;
using Ombi.Store.Repository;

namespace Ombi.Schedule.Tests
{
    [TestFixture]
    public class PlexEpisodeSyncTests
    {
        private AutoMocker _mocker;
        private PlexEpisodeSync _subject;
        private Mock<IPlexContentRepository> _repoMock;
        private List<List<IMediaServerEpisode>> _addedBatches;

        [SetUp]
        public void Setup()
        {
            _mocker = new AutoMocker();
            _subject = _mocker.CreateInstance<PlexEpisodeSync>();
            _repoMock = _mocker.GetMock<IPlexContentRepository>();
            _addedBatches = new List<List<IMediaServerEpisode>>();
            _repoMock.Setup(r => r.AddRange(It.IsAny<IEnumerable<IMediaServerEpisode>>()))
                .Callback<IEnumerable<IMediaServerEpisode>>(episodes => _addedBatches.Add(episodes.ToList()))
                .Returns(Task.CompletedTask);
            // The series exists, so the title-match fallback is not taken.
            _repoMock.Setup(r => r.GetByKey(It.IsAny<string>()))
                .ReturnsAsync(new PlexServerContent { Key = "10" });
            _repoMock.Setup(r => r.GetAll())
                .Returns(new List<PlexServerContent>().AsQueryable().BuildMock());
        }

        private static Metadata Episode(string ratingKey, string grandparentRatingKey) =>
            new Metadata
            {
                ratingKey = ratingKey,
                grandparentRatingKey = grandparentRatingKey,
                grandparentTitle = "Show",
                title = "Episode " + ratingKey,
                index = 1,
                parentIndex = 1,
                parentRatingKey = "5",
            };

        private static IQueryable<PlexEpisode> ExistingEpisodes() =>
            new List<PlexEpisode>
            {
                new PlexEpisode { Key = "1", GrandparentKey = "10" },
            }.AsQueryable().BuildMock();

        [Test]
        public async Task ProcessEpsiodes_WithSharedKeySet_ShouldNotReAddEpisodesSavedByAnEarlierBatch()
        {
            // The mocked queryable never changes, so without the shared set
            // the second batch would try to add episode "2" again. The set
            // is updated when the first batch saves, so it must not.
            var sharedKeys = new HashSet<(string Key, string GrandparentKey)>();
            var currentEpisodes = ExistingEpisodes();

            await _subject.ProcessEpsiodes(new[] { Episode("2", "10") }, currentEpisodes, sharedKeys);
            await _subject.ProcessEpsiodes(new[] { Episode("2", "10"), Episode("3", "10") }, currentEpisodes, sharedKeys);

            Assert.Multiple(() =>
            {
                Assert.That(_addedBatches.Count, Is.EqualTo(2));
                Assert.That(_addedBatches[0].Select(x => ((PlexEpisode)x).Key), Is.EqualTo(new[] { "2" }));
                Assert.That(_addedBatches[1].Select(x => ((PlexEpisode)x).Key), Is.EqualTo(new[] { "3" }));
            });
        }

        [Test]
        public async Task ProcessEpsiodes_WithoutSharedKeySet_ShouldReloadKeysOnEveryCall()
        {
            // No shared set: each call loads the keys from the queryable,
            // which the mocked AddRange never persists into, so the second
            // call tries to add episode "2" again.
            var currentEpisodes = ExistingEpisodes();

            await _subject.ProcessEpsiodes(new[] { Episode("2", "10") }, currentEpisodes);
            await _subject.ProcessEpsiodes(new[] { Episode("2", "10") }, currentEpisodes);

            Assert.Multiple(() =>
            {
                Assert.That(_addedBatches.Count, Is.EqualTo(2));
                Assert.That(_addedBatches[1].Select(x => ((PlexEpisode)x).Key), Is.EqualTo(new[] { "2" }));
            });
        }
    }
}
