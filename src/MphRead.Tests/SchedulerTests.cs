using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed class SchedulerTests
    {
        [Fact]
        public void AbsoluteDeadlinesDoNotAccumulatePollingDelay()
        {
            var scheduler = new FixedTickScheduler(0, 60000);
            int ticks = 0;
            // A caller always 7 ms late still executes 60 steps per second.
            for (int now = 7; now <= 60007; now += 10)
            {
                ticks += scheduler.TakeDue(now * 60L);
            }
            Assert.Equal(3600, ticks);
            Assert.Equal(0, scheduler.DroppedTicks);
        }

        [Fact]
        public void StallResynchronizesAfterBoundedCatchUp()
        {
            var scheduler = new FixedTickScheduler(0, 60000);
            Assert.Equal(0, scheduler.TakeDue(999));
            Assert.Equal(1, scheduler.TakeDue(1000));
            Assert.Equal(4, scheduler.TakeDue(121000));
            Assert.Equal(116, scheduler.DroppedTicks);
            Assert.Equal(1, scheduler.Overloads);
            Assert.Equal(0, scheduler.TakeDue(121000));
            Assert.Equal(1, scheduler.TakeDue(122000));
        }
    }
}
