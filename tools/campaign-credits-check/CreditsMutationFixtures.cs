using MphRead;
using ModelSource=MphRead.Mods.Campaign.CampaignCreditsModelSource;
using Binding=MphRead.Mods.Campaign.CampaignCreditsAnimationBinding;
using Duration=MphRead.Mods.Campaign.CampaignCreditsAnimationDuration;

// Deterministic corruptions verify that admission is sufficient for the
// existing decoder, including accepted mutations in ignored/padding fields.
internal static class CreditsMutationFixtures
{
    internal static void Run()
    {
        var random = new Random(731294); var originalModel = CreditsRenderFixture.Model();
        var originalAnimation = CreditsRenderFixture.Animation(); var model = ModelSource.Read(originalModel.Bytes);
        int acceptedModels = 0, acceptedAnimations = 0, rejected = 0;
        int caches = Read.CachedModels.Count();
        for (int trial = 0; trial < 2000; trial++)
        {
            bool animation = trial % 2 == 0;
            byte[] bytes = (byte[])(animation ? originalAnimation.Bytes : originalModel.Bytes).Clone();
            for (int mutation = 0; mutation <= trial % 3; mutation++) bytes[random.Next(bytes.Length)] = (byte)random.Next(256);
            if (animation)
            {
                Binding.Snapshot proof;
                try { proof = Binding.Read(model, Duration.Read(bytes), bytes); }
                catch (InvalidDataException) { rejected++; continue; }
                // Fail if any admitted corruption reaches a decoder exception.
                _ = Read.ReadCreditsModel(model, proof); acceptedAnimations++;
            }
            else
            {
                ModelSource.Snapshot proof;
                try { proof = ModelSource.Read(bytes); }
                catch (InvalidDataException) { rejected++; continue; }
                _ = Read.ReadCreditsModel(proof); acceptedModels++;
            }
        }
        if (Read.CachedModels.Count() != caches || acceptedModels == 0 || acceptedAnimations == 0 || rejected == 0)
            throw new InvalidOperationException("Mutation probe failed to exercise admission/decoder/cache boundaries.");
        Console.WriteLine($"CREDITS MUTATION PASS 2000 corruptions: {rejected} bounded refusals, {acceptedModels} decoded models, {acceptedAnimations} decoded bindings; cache unchanged.");
    }
}
