using System;
using MphRead;
using Xunit;

namespace MphRead.Tests
{
    [Collection("Match baseline globals")]
    public sealed class SharedEntityStateTests
    {
        [Fact]
        public void InitialEntityStateUsesLegacyRoomAndEntitySentinels()
        {
            using SceneState state = new();

            state.Scene.RoomId = 26;
            Assert.Equal(0, state.Scene.GetInitialEntityState(10, active: true));

            state.Scene.RoomId = 100;
            Assert.Equal(0, state.Scene.GetInitialEntityState(-1, active: true));

            state.Scene.RoomId = 93;
            Assert.Equal(1, state.Scene.GetInitialEntityState(10, active: true));

            state.Scene.RoomId = 50;
            Assert.Equal(1, state.Scene.GetInitialEntityState(240, active: true));
        }

        [Fact]
        public void InitialEntityStateUsesActiveStateValuesForInRangeEntities()
        {
            using SceneState state = new();
            state.Scene.RoomId = 27;

            Assert.Equal(2, state.Scene.GetInitialEntityState(10, active: true));
            Assert.Equal(0, state.Scene.GetInitialEntityState(10, active: false));
            Assert.Equal(1, state.Scene.GetInitialEntityState(10, active: true, activeState: 2));
        }

        [Fact]
        public void TriggerStateStartsEmptyAndBitsRemainIndependentPerScene()
        {
            using SceneState first = new();
            SetTrigger(first.Scene, 0);
            SetTrigger(first.Scene, 31);

            Assert.True(first.Scene.IsTriggerStateSet(0));
            Assert.True(first.Scene.IsTriggerStateSet(31));

            ClearTrigger(first.Scene, 0);
            Assert.False(first.Scene.IsTriggerStateSet(0));
            Assert.True(first.Scene.IsTriggerStateSet(31));

            using SceneState second = new();
            Assert.False(second.Scene.IsTriggerStateSet(0));
            Assert.False(second.Scene.IsTriggerStateSet(31));
        }

        [Fact]
        public void TriggerStateRejectsIndexesOutsideItsThirtyTwoBitRange()
        {
            using SceneState state = new();

            Assert.Throws<ProgramException>(() => state.Scene.IsTriggerStateSet(-1));
            Assert.Throws<ProgramException>(() => state.Scene.IsTriggerStateSet(32));
            Assert.Throws<ProgramException>(() => SetTrigger(state.Scene, -1));
            Assert.Throws<ProgramException>(() => ClearTrigger(state.Scene, 32));
        }

        private static void SetTrigger(Scene scene, int index)
        {
            scene.SendMessage(Message.SetTriggerState, null!, null, index, null!, delay: 0);
        }

        private static void ClearTrigger(Scene scene, int index)
        {
            scene.SendMessage(Message.ClearTriggerState, null!, null, index, null!, delay: 0);
        }

        private sealed class SceneState : IDisposable
        {
            public Scene Scene { get; } = Scene.CreateHeadless();

            public void Dispose()
            {
                Scene.CloseHeadless();
            }
        }
    }
}
