using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead.Mods.MapGen;
using Xunit;

namespace MphRead.Tests
{
    [CollectionDefinition("custom room loader", DisableParallelization = true)]
    public sealed class CustomRoomLoaderCollection
    {
    }

    [Collection("custom room loader")]
    public sealed class RoomCatalogTests
    {
        [Fact]
        public void CatalogRetainsSparseBuiltInGlobalsAndAppendsCustomRooms()
        {
            Assert.Equal(39, Metadata.RoomList.Count(room =>
                room.Id < RuntimeRoomRegistry.FirstCustomRoomId));

            int[] builtInIds = Enumerable.Range(93, 27)
                .Concat(new[] { 122, 123, 124, 125, 126, 127, 128, 129, 130, 135, 136, 137 })
                .ToArray();
            Assert.Equal(39, builtInIds.Length);
            foreach (int id in builtInIds)
            {
                RoomMetadata? room = Metadata.GetRoomById(id);
                Assert.NotNull(room);
                if (id < 128) { Assert.Equal(id, room!.Id); }
            }
        }

        [Theory]
        [InlineData(128, 0)]
        [InlineData(129, 1)]
        [InlineData(130, 2)]
        [InlineData(135, 7)]
        [InlineData(136, 8)]
        [InlineData(137, 9)]
        public void FirstHuntGlobalsMapToTheirRetainedLocalMetadata(int globalId, int localId)
        {
            RoomMetadata? room = Metadata.GetRoomById(globalId);

            Assert.NotNull(room);
            Assert.Equal(localId, room!.Id);
            Assert.Null(Metadata.GetRoomById(localId, noThrow: true));
        }

        [Theory]
        [InlineData(120)]
        [InlineData(121)]
        [InlineData(131)]
        [InlineData(132)]
        [InlineData(133)]
        [InlineData(134)]
        public void RemovedGlobalRoomHolesDoNotResolveToDenseCatalogEntries(int id)
        {
            Assert.Null(Metadata.GetRoomById(id, noThrow: true));
        }

        [Fact]
        public void CustomRoomIdsComeFromTheRuntimeRegistry()
        {
            foreach (MapDefinition definition in CustomRooms.Definitions)
            {
                RuntimeRoomRegistration registration =
                    Assert.IsType<RuntimeRoomRegistration>(
                        Metadata.GetRuntimeRoomByName(definition.Name,
                            contentIdentity: null));

                Assert.True(registration.IsCustom);
                Assert.True(registration.RuntimeId
                    >= RuntimeRoomRegistry.FirstCustomRoomId);
                Assert.Equal(registration.RuntimeId,
                    registration.Metadata.Id);
                Assert.Equal(definition.Name, registration.RoomKey);
            }
        }

        [Fact]
        public void RuntimeCatalogRejectsAnUnallocatedId()
        {
            int unallocated = Metadata.RoomList
                .Where(room => room.Id >= RuntimeRoomRegistry.FirstCustomRoomId)
                .Select(room => room.Id)
                .DefaultIfEmpty(RuntimeRoomRegistry.FirstCustomRoomId - 1)
                .Max() + 1;

            Assert.Null(Metadata.GetRoomById(unallocated, noThrow: true));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Metadata.GetRoomById(unallocated));
        }

        [Fact]
        public void MetadataOnlyNestedJsonIsIgnoredWhileRealDefinitionsRemain()
        {
            string previousDirectory = CustomRooms.MapDirectory;
            string temporaryDirectory = Path.Combine(Path.GetTempPath(),
                "project-prime-room-loader-" + Guid.NewGuid().ToString("N"));
            string reportDirectory = Path.Combine(temporaryDirectory, "reports", "nested");
            Directory.CreateDirectory(reportDirectory);

            try
            {
                File.WriteAllText(Path.Combine(reportDirectory, "hash-report.json"),
                    "{\"mapSha256\":\"abc\",\"nested\":{\"bspSha256\":\"def\"}}");
                File.WriteAllText(Path.Combine(temporaryDirectory, "brush.json"),
                    "{\"name\":\"brush map\","
                    + "\"materials\":[{\"name\":\"fixture\",\"sourceMaterial\":0}],"
                    + "\"brushes\":[{\"min\":[0,0,0],\"max\":[1,1,1]}],"
                    + "\"spawns\":[{\"position\":[-2,2,0]},{\"position\":[2,2,0]}]}");
                File.WriteAllText(Path.Combine(temporaryDirectory, "level.bsp"), "fixture");
                File.WriteAllText(Path.Combine(temporaryDirectory, "import.json"),
                    "{\"name\":\"import map\",\"import\":{\"source\":\"level.bsp\"}}");

                CustomRooms.MapDirectory = temporaryDirectory;
                MethodInfo loader = typeof(CustomRooms).GetMethod("LoadDefinitions",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                IReadOnlyList<MapDefinition> definitions =
                    (IReadOnlyList<MapDefinition>)loader.Invoke(null, null)!;

                Assert.DoesNotContain(definitions, definition => definition.Name == "CUSTOM");
                MapDefinition brush = Assert.Single(definitions,
                    definition => definition.Name == "BRUSH MAP");
                Assert.Single(brush.Brushes);
                MapDefinition imported = Assert.Single(definitions,
                    definition => definition.Name == "IMPORT MAP");
                Assert.NotNull(imported.Import);
                Assert.Equal("level.bsp", imported.Import!.Source);
            }
            finally
            {
                CustomRooms.MapDirectory = previousDirectory;
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
            }
        }
    }
}
