namespace ProjectPrime.MapPlatform.Tests;

/// <summary>
/// Direct runner for restricted environments where VSTest cannot bind its local
/// coordination socket. CI still discovers the same xUnit tests normally.
/// </summary>
internal static class Program
{
    public static async Task<int> Main()
    {
        int passed = 0;
        await Run(async test => await test.V2BundleIsByteDeterministicAndVerifiesBothIdentities());
        foreach (string path in new[]
        {
            "../evil.bin", "/absolute.bin", "C:/drive.bin", "nested\\ambiguous.bin",
            "nested//empty.bin", "./relative.bin", "native.dll", "script.sh"
        })
            await Run(test => { test.UnsafeOrExecutablePathsAreRejected(path); return Task.CompletedTask; });
        await Run(test => { test.MissingManifestIsRejectedWhenLegacyMigrationIsDisabled(); return Task.CompletedTask; });
        await Run(test => { test.LegacyMigrationRequiresExactlyOneRecipe(); return Task.CompletedTask; });
        await Run(test => { test.DuplicateAndCaseCollidingPathsAreRejected(); return Task.CompletedTask; });
        await Run(test => { test.SymlinkEntryIsRejected(); return Task.CompletedTask; });
        await Run(test => { test.FileCountEntrySizeTotalSizeAndCompressionRatioAreBounded(); return Task.CompletedTask; });
        await Run(async test => await test.WriterRejectsOversizedInputBeforePublishingDestination());
        await Run(async test => await test.WrongHashAndUndeclaredFileAreRejected());
        await Run(test => { test.CorruptZipMalformedJsonAndDuplicateJsonMembersFailSafely(); return Task.CompletedTask; });
        await Run(async test => await test.UnknownManifestMemberAndUnexpectedJsonRoleAreRejected());
        await Run(async test => await test.StructurallyValidPackageWithInvalidMapRecipeFailsSemanticVerification());
        await Run(async test => await test.DuplicateMembersInV2RecipeFailSemanticVerification());
        await Run(async test => await test.CaseVariantMembersInV2RecipeFailSemanticVerification());
        await Run(async test => await test.RecipeIdentityCannotDisagreeWithManifestIdentity());

        await RunCatalog(async test => await test.InstallRefreshAndRemoveUseStableIdentityWithoutTouchingSource());
        await RunCatalog(async test => await test.RefreshPublishesANewImmutableSnapshot());
        await RunCatalog(async test => await test.ValidCacheRestoresReadyStateAndRemovalPrunesOnlyItsCache());
        await RunCatalog(async test => await test.EditableProjectAndInstalledPackageWithSameIdentityCoexist());
        await RunCatalog(async test => await test.MissingImportedGeometryRemainsVisibleAsRepairableCatalogState());
        await RunCatalog(async test => await test.InstallRejectsSemanticallyInvalidPackageWithoutPublishingOrCopyingIt());

        await RunCompiler(test => { test.StableIdentityAndCanonicalSourceHashingAreStrict(); return Task.CompletedTask; });
        foreach (object[] values in MapCompilerTests.FirstPartyMaps)
            await RunCompiler(async test => await test.FirstPartyMapOutputRemainsCharacterized(
                (FirstPartyMapCharacterization)values[0]));
        await RunCompiler(test => { test.ModeSpawnMaterialAndFixedPointValidationProducesStructuredCodes(); return Task.CompletedTask; });
        await RunCompiler(test => { test.NativeSpawnValidationDetectsSolidIntersectionsAndOverlap(); return Task.CompletedTask; });
        await RunCompiler(test => { test.NativeMaterialValidationRejectsAmbiguousSourceAndUnknownTerrain(); return Task.CompletedTask; });
        await RunCompiler(test => { test.CollisionPackingIsByteDeterministicAndReportsGridMetrics(); return Task.CompletedTask; });
        await RunCompiler(test => { test.CaptureBountyAndNodesCompileToTypedRuntimeEntities(); return Task.CompletedTask; });
        await RunCompiler(async test => await test.CompileSameMapTwiceUsesValidatedContentAddressedCache());
        await RunCompiler(async test => await test.ConcurrentCompilersAtomicallyPublishOneValidCache());
        await RunCompiler(test => { test.NativeConvexBrushesProduceUnifiedRenderCollisionAndTypedEntities(); return Task.CompletedTask; });
        await RunCompiler(async test => await test.NativeProjectCompilesByteDeterministicallyAndRoundTripsThroughV2Package());
        await RunCompiler(async test => await test.NativeCustomImagesAndDamageVolumesCompileAndTravelInsidePackage());
        await RunCompiler(test => { test.ProjectContentIdentityIncludesExternalDependencyBytesButNotTimestamps(); return Task.CompletedTask; });
        await RunCompiler(test => { test.BuildFingerprintIncludesOnlyRelevantBaseContentIdentity(); return Task.CompletedTask; });
        await RunCompiler(async test => await test.AmbientMatchMountsAreIsolatedAcrossConcurrentExecutionContexts());

        await RunEditor(test => { test.NewProjectStartsWithEditableArenaAndStableIdentity(); return Task.CompletedTask; });
        await RunEditor(test => { test.CommandsRoundTripThroughUndoAndRedo(); return Task.CompletedTask; });
        await RunEditor(test => { test.AutosaveRecoveryNeverOverwritesCreatorProject(); return Task.CompletedTask; });
        await RunEditor(test => { test.PlaytestSnapshotDoesNotChangeDocumentPathOrDirtyState(); return Task.CompletedTask; });
        await RunEditor(test => { test.AuthoringEnvironmentBecomesTheCompiledRuntimeEnvironment(); return Task.CompletedTask; });
        await RunEditor(test => { test.MaterialCommandTargetsOneFaceOrTheWholeBrushAndUndoRestoresIt(); return Task.CompletedTask; });
        await RunEditor(test => { test.Q3FactoryCreatesAReadOnlyImportedProjectWithPortableRelativeSource(); return Task.CompletedTask; });

        Console.WriteLine($"Map platform self-test passed ({passed} cases). VSTest remains the CI runner.");
        return 0;

        async Task Run(Func<MapPackageSecurityTests, Task> action)
        {
            using var test = new MapPackageSecurityTests();
            await action(test);
            passed++;
        }
        async Task RunCatalog(Func<MapCatalogTests, Task> action)
        {
            using var test = new MapCatalogTests();
            await action(test);
            passed++;
        }
        async Task RunCompiler(Func<MapCompilerTests, Task> action)
        {
            using var test = new MapCompilerTests();
            await action(test);
            passed++;
        }
        async Task RunEditor(Func<EditorDocumentTests, Task> action)
        {
            using var test = new EditorDocumentTests();
            await action(test);
            passed++;
        }
    }
}
