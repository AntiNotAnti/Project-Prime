using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    public sealed class PredictionTests
    {
        [Fact]
        public void CorrectsHistoricalErrorWithoutApplyingTheSameTranslationTwice()
        {
            var prediction = new ClientPrediction();
            prediction.Record(10, new Vector3(10, 0, 0), false);
            prediction.Record(11, new Vector3(11, 0, 0), false);
            Vector3 current = new(12, 0, 0);
            current = prediction.Reconcile(10, new Vector3(9, 0, 0), false, current);
            Assert.Equal(new Vector3(11, 0, 0), current);
            Assert.Equal(Vector3.UnitX, prediction.VisualOffset);
            current = prediction.Reconcile(11, new Vector3(10, 0, 0), false, current);
            Assert.Equal(new Vector3(11, 0, 0), current);
            Assert.Equal(1, prediction.Corrections);
            Assert.Equal(current, prediction.Reconcile(10, Vector3.Zero, false, current));
            prediction.AdvanceVisual(0.2);
            Assert.InRange(prediction.VisualOffset.Length, 0, 0.02f);
        }

        [Fact]
        public void SequenceWrapAndTinyErrorsDoNotSnap()
        {
            var prediction = new ClientPrediction();
            prediction.Record(UInt32.MaxValue, Vector3.Zero, false);
            prediction.Record(0, Vector3.UnitX, false);
            Assert.Equal(Vector3.UnitX, prediction.Reconcile(UInt32.MaxValue,
                new Vector3(0.001f, 0, 0), false, Vector3.UnitX));
            Assert.Equal(Vector3.UnitX, prediction.Reconcile(0, Vector3.UnitX, false, Vector3.UnitX));
            Assert.Equal(0, prediction.Corrections);
            Assert.Equal(0, prediction.HardCorrections);
        }

        [Fact]
        public void DivergenceInvalidPredictionAndFormMismatchSnapAndClearVisualError()
        {
            foreach (int reason in new[] { 0, 1, 2 })
            {
                var prediction = new ClientPrediction();
                prediction.Record(1, Vector3.Zero, false);
                Vector3 server = reason == 0 ? new Vector3(50, 0, 0) : Vector3.UnitX;
                Vector3 current = reason == 1 ? new Vector3(Single.NaN, 0, 0) : Vector3.Zero;
                Assert.Equal(server, prediction.Reconcile(1, server, reason == 2, current));
                Assert.True(prediction.LastCorrectionHard);
                Assert.Equal(Vector3.Zero, prediction.VisualOffset);
            }
        }

        [Fact]
        public void OverwrittenHistoryCannotAliasOldAcknowledgementAndResetClearsIt()
        {
            var prediction = new ClientPrediction();
            for (uint i = 0; i <= ClientPrediction.Capacity; i++)
            {
                prediction.Record(i, new Vector3(i, 0, 0), false);
            }
            Assert.Equal(Vector3.Zero, prediction.Reconcile(0, Vector3.Zero, false, Vector3.UnitX));
            Assert.True(prediction.LastCorrectionHard);
            prediction.Reset();
            prediction.Record(0, Vector3.Zero, false);
            Assert.Equal(Vector3.UnitX, prediction.Reconcile(0, Vector3.UnitX, false, Vector3.Zero));
            Assert.False(prediction.LastCorrectionHard);
        }
    }
}
