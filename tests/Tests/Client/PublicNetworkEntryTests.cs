using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    [Collection("Transport impairment")]
    public sealed class PublicNetworkEntryTests
    {
        [Fact]
        public void PublicJoinUsesCurrentProtocolAndGracefulStopReleasesTheServerSlot()
        {
            Assert.True(NetLag.Configure("200"));
            using var transport = new NetTransport(0);
            var server = new ServerNetwork(transport, "MP1 SANCTORUS", GameMode.Battle);
            using var stop = new CancellationTokenSource();
            Exception? failure = null;
            var owner = new Thread(() =>
            {
                try
                {
                    var clock = Stopwatch.StartNew();
                    while (!stop.IsCancellationRequested)
                    {
                        server.Poll((uint)(clock.Elapsed.TotalSeconds * 60));
                        Thread.Sleep(2);
                    }
                }
                catch (Exception exception) { failure = exception; }
            }) { IsBackground = true };
            var cheats = new List<(PropertyInfo Property, bool Value)>();
            foreach (PropertyInfo property in typeof(Cheats).GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (property.PropertyType == typeof(bool) && property.CanRead && property.CanWrite)
                {
                    cheats.Add((property, (bool)property.GetValue(null)!));
                }
            }
            owner.Start();
            try
            {
                // Public joins advertise the current live protocol, v8.
                Assert.Equal(8, NetConfig.ProtocolVersion);
                Assert.True(NetProbe.Probe("127.0.0.1", transport.LocalPort).Ok);
                Assert.True(NetLaunch.Join("127.0.0.1", transport.LocalPort, "PUBLIC", Hunter.Samus));
                Assert.False(NetSession.Active);
                AuthoritativePlay play = Assert.IsType<AuthoritativePlay>(AuthoritativePlay.Current);
                Assert.Equal(NetConnectionState.Loading, play.Client.State);
                Assert.Equal(0, play.LocalSlot);
                Assert.Equal(("MP1 SANCTORUS", GameMode.Battle), NetLaunch.ServerRoom()!.Value);
                NetSession.Stop();
                Assert.Null(AuthoritativePlay.Current);
            }
            finally
            {
                AuthoritativePlay.Current?.Dispose();
                stop.Cancel();
                Assert.True(owner.Join(5000));
                foreach (var setting in cheats) { setting.Property.SetValue(null, setting.Value); }
                NetLag.Configure("0");
                NetLag.ConfigureLoss("0");
            }
            Assert.Null(failure);
            Assert.Equal(0, server.Count);
        }

        [Fact]
        public void CancelledJoinDoesNotCreateAnAuthoritativeSession()
        {
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.False(NetLaunch.Join("127.0.0.1", 27888, "CANCEL", Hunter.Samus, cancel: cancel.Token));
            Assert.Null(AuthoritativePlay.Current);
            Assert.Equal("Connection cancelled.", NetLaunch.LastJoinError);
        }
    }
}
