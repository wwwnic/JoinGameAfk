using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Services;
using NUnit.Framework;

namespace JoinGameAfk.Tests
{
    [TestFixture]
    public sealed class RolePlanProfileStoreTests
    {
        [Test]
        public void AddProfile_PreservesLeagueClassicMode()
        {
            RolePlanProfileStore store = CreateStore();

            RolePlanProfile saved = store.AddProfile(
                "Classic plan",
                RolePlanProfileSections.RolePlans,
                CreatePlans(103, 86),
                null,
                gameMode: LeagueGameMode.Classic);

            Assert.Multiple(() =>
            {
                Assert.That(saved.GameMode, Is.EqualTo(LeagueGameMode.Classic));
                Assert.That(store.LoadProfiles().Single().GameMode, Is.EqualTo(LeagueGameMode.Classic));
            });
        }

        private string _testDirectory = null!;
        private string _profileFilePath = null!;
        private string _iconDirectoryPath = null!;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "profile-tests",
                Guid.NewGuid().ToString("N"));
            _profileFilePath = Path.Combine(_testDirectory, "profiles.json");
            _iconDirectoryPath = Path.Combine(_testDirectory, "profile-icons");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testDirectory))
                Directory.Delete(_testDirectory, recursive: true);
        }

        [Test]
        public void AddProfile_AllowsDuplicateDisplayNamesWithDistinctIdentity()
        {
            var store = CreateStore();
            var plans = CreatePlans(103, 238);

            RolePlanProfile first = store.AddProfile(
                "Ranked",
                RolePlanProfileSections.RolePlans,
                plans,
                null);
            RolePlanProfile second = store.AddProfile(
                "Ranked",
                RolePlanProfileSections.RolePlans,
                plans,
                null);

            IReadOnlyList<RolePlanProfile> loaded = store.LoadProfiles();
            Assert.Multiple(() =>
            {
                Assert.That(loaded, Has.Count.EqualTo(2));
                Assert.That(loaded.Select(profile => profile.Name), Is.All.EqualTo("Ranked"));
                Assert.That(first.Id, Is.Not.EqualTo(second.Id));
                Assert.That(loaded.Select(profile => profile.Id).Distinct().Count(), Is.EqualTo(2));
            });
        }

        [Test]
        public void MoveProfile_PersistsCustomOrder()
        {
            var store = CreateStore();
            var plans = CreatePlans(103, 238);
            RolePlanProfile first = store.AddProfile("First", RolePlanProfileSections.RolePlans, plans, null);
            RolePlanProfile second = store.AddProfile("Second", RolePlanProfileSections.RolePlans, plans, null);
            RolePlanProfile third = store.AddProfile("Third", RolePlanProfileSections.RolePlans, plans, null);

            Assert.That(
                store.LoadProfiles().Select(profile => profile.Id),
                Is.EqualTo(new[] { third.Id, second.Id, first.Id }));

            store.MoveProfile(first.Id, -1);

            var reloadedStore = CreateStore();
            Assert.That(
                reloadedStore.LoadProfiles().Select(profile => profile.Id),
                Is.EqualTo(new[] { third.Id, first.Id, second.Id }));
        }

        [Test]
        public void AddProfile_CopiesExactIconAndOnlyIncludedSection()
        {
            Directory.CreateDirectory(_testDirectory);
            string sourceIconPath = Path.Combine(_testDirectory, "Ahri_0.jpg");
            byte[] expectedBytes = [0xFF, 0xD8, 0x01, 0x23, 0x45, 0xFF, 0xD9];
            File.WriteAllBytes(sourceIconPath, expectedBytes);
            var store = CreateStore();

            RolePlanProfile saved = store.AddProfile(
                "Classic art",
                RolePlanProfileSections.ChampionPictures,
                null,
                new Dictionary<int, string> { [103] = "Jade_Ahri.jpg" },
                iconChampionId: 103,
                iconSourcePath: sourceIconPath);

            RolePlanProfile loaded = store.LoadProfiles().Single();
            string copiedIconPath = store.GetIconPath(loaded)!;
            Assert.Multiple(() =>
            {
                Assert.That(saved.IconFileName, Is.EqualTo($"{saved.Id:N}.jpg"));
                Assert.That(File.ReadAllBytes(copiedIconPath), Is.EqualTo(expectedBytes));
                Assert.That(loaded.RolePlans, Is.Null);
                Assert.That(loaded.ChampionPictures, Is.EqualTo(new Dictionary<int, string> { [103] = "Jade_Ahri.jpg" }));
                Assert.That(loaded.IconChampionId, Is.EqualTo(103));
            });
        }

        [Test]
        public void AddProfile_SnapshotsInputAndDeleteRemovesCopiedIcon()
        {
            Directory.CreateDirectory(_testDirectory);
            string sourceIconPath = Path.Combine(_testDirectory, "tile.jpg");
            File.WriteAllBytes(sourceIconPath, [0xFF, 0xD8, 0xFF, 0xD9]);
            var plans = CreatePlans(103, 238);
            var store = CreateStore();

            RolePlanProfile saved = store.AddProfile(
                "Main setup",
                RolePlanProfileSections.RolePlans,
                plans,
                null,
                iconChampionId: 103,
                iconSourcePath: sourceIconPath);
            plans[Position.Mid].PickChampionIds.Clear();
            string copiedIconPath = store.GetIconPath(saved)!;

            Assert.That(store.LoadProfiles().Single().RolePlans![Position.Mid].PickChampionIds, Is.EqualTo(new[] { 103 }));
            Assert.That(File.Exists(copiedIconPath), Is.True);

            bool deleted = store.DeleteProfile(saved.Id);
            Assert.Multiple(() =>
            {
                Assert.That(deleted, Is.True);
                Assert.That(store.LoadProfiles(), Is.Empty);
                Assert.That(File.Exists(copiedIconPath), Is.False);
            });
        }

        [Test]
        public void UpdateProfile_ReplacesIncludedSnapshotsAndPreservesIdentity()
        {
            Directory.CreateDirectory(_testDirectory);
            string sourceIconPath = Path.Combine(_testDirectory, "tile.jpg");
            File.WriteAllBytes(sourceIconPath, [0xFF, 0xD8, 0xFF, 0xD9]);
            string updatedIconPath = Path.Combine(_testDirectory, "updated-tile.jpg");
            byte[] updatedIconBytes = [0xFF, 0xD8, 0x12, 0x34, 0xFF, 0xD9];
            File.WriteAllBytes(updatedIconPath, updatedIconBytes);
            var store = CreateStore();
            RolePlanProfile saved = store.AddProfile(
                "Main setup",
                RolePlanProfileSections.RolePlans | RolePlanProfileSections.ChampionPictures,
                CreatePlans(103, 238),
                new Dictionary<int, string> { [103] = "Ahri_0.jpg" },
                iconChampionId: 103,
                iconSourcePath: sourceIconPath);

            RolePlanProfile updated = store.UpdateProfile(
                saved.Id,
                "Alternate setup",
                RolePlanProfileSections.RolePlans | RolePlanProfileSections.ChampionPictures,
                CreatePlans(84, 157),
                new Dictionary<int, string> { [84] = "Akali_7.jpg" },
                iconChampionId: 103,
                iconSourcePath: updatedIconPath);
            RolePlanProfile loaded = store.LoadProfiles().Single();

            Assert.Multiple(() =>
            {
                Assert.That(updated.Id, Is.EqualTo(saved.Id));
                Assert.That(updated.Name, Is.EqualTo("Alternate setup"));
                Assert.That(updated.IncludedSections, Is.EqualTo(saved.IncludedSections));
                Assert.That(updated.IconChampionId, Is.EqualTo(103));
                Assert.That(updated.IconFileName, Is.EqualTo(saved.IconFileName));
                Assert.That(updated.CreatedAtUtc, Is.EqualTo(saved.CreatedAtUtc));
                Assert.That(updated.UpdatedAtUtc, Is.GreaterThanOrEqualTo(saved.UpdatedAtUtc));
                Assert.That(loaded.RolePlans![Position.Mid].PickChampionIds, Is.EqualTo(new[] { 84 }));
                Assert.That(loaded.ChampionPictures, Is.EqualTo(new Dictionary<int, string> { [84] = "Akali_7.jpg" }));
                Assert.That(File.ReadAllBytes(store.GetIconPath(loaded)!), Is.EqualTo(updatedIconBytes));
            });
        }

        [Test]
        public void UpdateProfile_CanChangeIncludedSections()
        {
            Directory.CreateDirectory(_testDirectory);
            string sourceIconPath = Path.Combine(_testDirectory, "tile.jpg");
            File.WriteAllBytes(sourceIconPath, [0xFF, 0xD8, 0xFF, 0xD9]);
            var store = CreateStore();
            RolePlanProfile saved = store.AddProfile(
                "Plans only",
                RolePlanProfileSections.RolePlans,
                CreatePlans(103, 238),
                null);

            RolePlanProfile updated = store.UpdateProfile(
                saved.Id,
                "Pictures only",
                RolePlanProfileSections.ChampionPictures,
                null,
                new Dictionary<int, string> { [84] = "Akali_7.jpg" },
                iconChampionId: 84,
                iconSourcePath: sourceIconPath);

            Assert.Multiple(() =>
            {
                Assert.That(updated.RolePlans, Is.Null);
                Assert.That(updated.ChampionPictures, Is.EqualTo(new Dictionary<int, string> { [84] = "Akali_7.jpg" }));
                Assert.That(updated.IncludedSections, Is.EqualTo(RolePlanProfileSections.ChampionPictures));
            });
        }

        [Test]
        public void AddProfile_RequiresANameAndAtLeastOneSection()
        {
            var store = CreateStore();
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => store.AddProfile(" ", RolePlanProfileSections.RolePlans, CreatePlans(1, 2), null),
                    Throws.TypeOf<ArgumentException>());
                Assert.That(
                    () => store.AddProfile("Empty", RolePlanProfileSections.None, null, null),
                    Throws.TypeOf<ArgumentException>());
            });
        }

        private RolePlanProfileStore CreateStore()
        {
            return new RolePlanProfileStore(_profileFilePath, _iconDirectoryPath);
        }

        private static Dictionary<Position, PositionPreference> CreatePlans(int pickChampionId, int banChampionId)
        {
            return new Dictionary<Position, PositionPreference>
            {
                [Position.Mid] = new()
                {
                    PickChampionIds = [pickChampionId],
                    BanChampionIds = [banChampionId]
                }
            };
        }
    }
}
