try
{
    if (args.Length == 2 && args[0] == "--probe") return OwnedProbe.Run(args[1]);
    if (args.Length == 2 && args[0] == "--probe-timeline") return OwnedTimelineProbe.Run(args[1]);
    if (args.Length == 2 && args[0] == "--probe-render") return OwnedRenderProbe.Run(args[1]);
    if (args.Length == 2 && args[0] == "--probe-bitmaps") return OwnedBitmapProbe.Run(args[1]);
    if (args.Length == 2 && args[0] == "--probe-widgets") return OwnedRenderProbe.Run(args[1], sampleWidgets: true);
    if (args.Length == 2 && args[0] == "--probe-frames") return OwnedFrameProbe.Run(args[1]);
    if (args.Length == 2 && args[0] == "--probe-load") return OwnedLoadProbe.Run(args[1]);
    if (args.Length != 0) throw new ArgumentException("Use no arguments for generated fixtures, --probe OWNED_MPH_ROOT for source metadata, or --probe-timeline OWNED_MPH_ROOT for pure timeline metadata, or --probe-render OWNED_MPH_ROOT for model/animation admission metadata, or --probe-bitmaps OWNED_MPH_ROOT for CPU bitmap/font metadata, or --probe-widgets OWNED_MPH_ROOT for CPU widget geometry, or --probe-frames OWNED_MPH_ROOT for bounded CPU ending frames, or --probe-load OWNED_MPH_ROOT for production-loader metadata.");
    CreditsFixtures.Run();
    AnimationDurationFixtures.Run();
    CreditsTimelineFixtures.Run();
    CreditsModelFixtures.Run();
    CreditsAnimationBindingFixtures.Run();
    CreditsDecodeFixtures.Run();
    CreditsMutationFixtures.Run();
    CreditsBitmapFixtures.Run();
    CreditsTextFixtures.Run();
    CreditsWidgetFixtures.Run();
    CreditsFrameFixtures.Run();
    CreditsLoadFixtures.Run();
    CreditsClockFixtures.Run();
    CanvasAdapterFixtures.Run();
    CreditsPresentationFixtures.Run();
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("CREDITS SOURCE FAIL: " + error);
    return 1;
}
