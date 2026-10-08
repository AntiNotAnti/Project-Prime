#!/usr/bin/env python3
"""Deterministic IBus engine, only for an isolated acceptance-test D-Bus session."""
import pathlib
import sys

import gi
gi.require_version("IBus", "1.0")
from gi.repository import GLib, IBus


class PrimeFixture(IBus.Engine):
    __gtype_name__ = "PrimeRmlUiInputFixture"

    def do_process_key_event(self, keyval, keycode, state):
        if state & IBus.ModifierType.RELEASE_MASK:
            return False
        if keyval == ord("a"):
            self.update_preedit_text(IBus.Text.new_from_string("日本😀"), 2, True)
        elif keyval == ord("b"):
            self.update_preedit_text(IBus.Text.new_from_string("日"), 1, True)
        elif keyval == ord("c"):
            self.commit_text(IBus.Text.new_from_string("日本"))
            self.hide_preedit_text()
        elif keyval == ord("d"):
            self.update_preedit_text(IBus.Text.new_from_string("取消"), 2, True)
        elif keyval == ord("x"):
            self.commit_text(IBus.Text.new_from_string("λ😀"))
        else:
            return False
        return True

    def do_reset(self):
        self.hide_preedit_text()


IBus.init()
bus = IBus.Bus.new()
if not bus.is_connected():
    raise RuntimeError("Fixture could not connect to isolated IBus daemon")
factory = IBus.Factory.new(bus.get_connection())
factory.add_engine("prime-rmlui-test", PrimeFixture.__gtype__)
component = IBus.Component.new("org.projectprime.RmlUiTest", "RmlUi input contract fixture", "1", "MIT",
                               "Project Prime", "", "", "")
component.add_engine(IBus.EngineDesc.new("prime-rmlui-test", "RmlUi input fixture", "Deterministic input acceptance",
                                       "en", "MIT", "Project Prime", "", "us"))
if not bus.register_component(component):
    raise RuntimeError("Fixture component registration failed")
pathlib.Path(sys.argv[1]).write_text("ready", encoding="utf-8")
GLib.MainLoop().run()
