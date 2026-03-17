using NUnit.Framework;

namespace Precondition.LoopLab.Tests
{
    public sealed class LoopLabRenderSettingsTests
    {
        [Test]
        public void GetValidated_UsesDefaultSeedWhenSeedIsMissing()
        {
            var settings = new LoopLabRenderSettings
            {
                Preset = LoopLabPresetKind.Landscape,
                FramesPerSecond = 24,
                DurationSeconds = 3f,
                Resolution = 512,
                Seed = 0
            };

            var validated = settings.GetValidated();

            Assert.That(validated.Seed, Is.EqualTo(LoopLabRenderSettings.DefaultSeedValue));
        }

        [TestCase(-42)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void ValidateSeed_NormalizesUnsupportedValuesIntoStableFloatSafeRange(int seed)
        {
            var validatedSeed = LoopLabRenderSettings.ValidateSeed(seed);

            Assert.That(validatedSeed, Is.GreaterThan(0));
            Assert.That(validatedSeed, Is.LessThanOrEqualTo(LoopLabRenderSettings.MaxSupportedSeedValue));
            Assert.That(LoopLabRenderSettings.ValidateSeed(validatedSeed), Is.EqualTo(validatedSeed));
        }

        [Test]
        public void CalculateFrameCount_UsesValidatedDurationAndFps()
        {
            Assert.That(LoopLabRenderSettings.CalculateFrameCount(2.5f, 24), Is.EqualTo(60));
            Assert.That(LoopLabRenderSettings.CalculateFrameCount(0f, 0), Is.EqualTo(1));
            Assert.That(LoopLabRenderSettings.CalculateFrameCount(float.NaN, -8), Is.EqualTo(1));
        }

        [Test]
        public void GetPreviewFrameIndex_WrapsCleanlyAcrossLoopBoundary()
        {
            var frameCount = LoopLabRenderSettings.CalculateFrameCount(3f, 24);

            Assert.That(LoopLabRenderSettings.GetPreviewFrameIndex(0f, 3f, frameCount), Is.EqualTo(0));
            Assert.That(LoopLabRenderSettings.GetPreviewFrameIndex(3f, 3f, frameCount), Is.EqualTo(0));
            Assert.That(LoopLabRenderSettings.GetPreviewFrameIndex(2.99f, 3f, frameCount), Is.EqualTo(frameCount - 1));
        }

        [Test]
        public void NormalizeFrameIndex_WrapsNegativeAndOverflowFrameNumbers()
        {
            Assert.That(LoopLabRenderSettings.NormalizeFrameIndex(0, 24), Is.EqualTo(0));
            Assert.That(LoopLabRenderSettings.NormalizeFrameIndex(24, 24), Is.EqualTo(0));
            Assert.That(LoopLabRenderSettings.NormalizeFrameIndex(25, 24), Is.EqualTo(1));
            Assert.That(LoopLabRenderSettings.NormalizeFrameIndex(-1, 24), Is.EqualTo(23));
            Assert.That(LoopLabRenderSettings.NormalizeFrameIndex(-25, 24), Is.EqualTo(23));
            Assert.That(LoopLabRenderSettings.NormalizeFrameIndex(42, 1), Is.EqualTo(0));
        }

        [Test]
        public void GetValidated_ProducesStableNormalizedGenerationInputs()
        {
            var settings = new LoopLabRenderSettings
            {
                Preset = LoopLabPresetKind.Fluid,
                FramesPerSecond = 0,
                DurationSeconds = -5f,
                Resolution = 8192,
                Seed = int.MinValue
            };

            var first = settings.GetValidated();
            var second = settings.GetValidated();

            Assert.That(first.Preset, Is.EqualTo(settings.Preset));
            Assert.That(first.FramesPerSecond, Is.EqualTo(LoopLabRenderSettings.MinimumFramesPerSecond));
            Assert.That(first.DurationSeconds, Is.EqualTo(LoopLabRenderSettings.MinimumDurationSeconds));
            Assert.That(first.Resolution, Is.EqualTo(2048));
            Assert.That(first.Seed, Is.EqualTo(second.Seed));
            Assert.That(first.FrameCount, Is.EqualTo(second.FrameCount));
        }

        [Test]
        public void RandomizeSeed_ProducesStableNextSeedInSupportedRange()
        {
            const int seed = 1337;

            var firstRandomizedSeed = LoopLabRenderSettings.RandomizeSeed(seed);
            var secondRandomizedSeed = LoopLabRenderSettings.RandomizeSeed(seed);
            var nextRandomizedSeed = LoopLabRenderSettings.RandomizeSeed(firstRandomizedSeed);

            Assert.That(firstRandomizedSeed, Is.EqualTo(secondRandomizedSeed));
            Assert.That(firstRandomizedSeed, Is.Not.EqualTo(LoopLabRenderSettings.ValidateSeed(seed)));
            Assert.That(firstRandomizedSeed, Is.GreaterThan(0));
            Assert.That(firstRandomizedSeed, Is.LessThanOrEqualTo(LoopLabRenderSettings.MaxSupportedSeedValue));
            Assert.That(nextRandomizedSeed, Is.Not.EqualTo(firstRandomizedSeed));
        }
    }
}
