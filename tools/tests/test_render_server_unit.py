import importlib.util
import pathlib
import shlex
import subprocess
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("render_server_unit", ROOT / "tools/render-server-unit.py")
renderer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(renderer)


class ServerUnitTests(unittest.TestCase):
    def test_new_server_has_explicit_content_and_stays_unlisted(self):
        source = (ROOT / "tools/systemd/mphread-server.service").read_text()
        result = renderer.render(source, "gameuser", "/opt/fruity", "/srv/content", "AMHE1", None)
        self.assertNotIn("__", result)
        self.assertIn('-data "/srv/content"', result)
        self.assertIn('-dataversion "AMHE1"', result)
        self.assertIn("-nomaster", result)

    def test_existing_rules_and_quoted_flag_text_survive(self):
        source = '[Service]\nExecStart="/old dir/MphRead" -server -port 29999 -servername "Cup -data not-a-path" -friendlyfire -data "/old data" -dataversion AMHE0 -master old.example:27889\n'
        result = renderer.render(source, "gameuser", "/new dir", "/new data", "AMHE1", None)
        self.assertIn('-servername "Cup -data not-a-path"', result)
        self.assertIn("-port 29999", result)
        self.assertIn("-friendlyfire", result)
        self.assertIn("-master old.example:27889", result)
        self.assertNotIn("/old data", result)
        self.assertIn('ExecStart="/new dir/FruityPrime"', result)

    def test_explicit_listing_replaces_only_listing_flags(self):
        source = '[Service]\nExecStart=/old/FruityPrime -server -nomaster -master old.example -masterport 29999 -servername "Cup -master text"\n'
        result = renderer.render(source, "gameuser", "/opt/fruity", "/srv/content", "AMHE1", "games.example.com:27889")
        self.assertNotIn("-nomaster", result)
        self.assertNotIn("-masterport", result)
        self.assertIn('-master "games.example.com:27889"', result)
        self.assertIn('-servername "Cup -master text"', result)

    def test_systemd_percent_and_shell_metacharacters_are_literal(self):
        source = '[Service]\nExecStart=/old/FruityPrime -server -servername "%H server"\n'
        value = '/srv/space "quote" %value $(printf changed)'
        result = renderer.render(source, "gameuser", "/opt/fruity", value, "AMHE1", None)
        self.assertIn('-servername "%H server"', result)
        self.assertIn('%%value', result)
        arguments = shlex.split(result.split("ExecStart=", 1)[1])
        self.assertEqual(arguments[arguments.index("-data") + 1].replace("%%", "%"), value)

    def test_master_template_remains_data_free(self):
        source = (ROOT / "tools/systemd/mphread-master.service").read_text()
        result = renderer.render(source, "gameuser", "/opt/fruity", None, "AMHE1", None)
        command = result.split("ExecStart=", 1)[1].splitlines()[0]
        self.assertIn("-hostports none", command)
        self.assertNotIn("-data", command)

    def test_unknown_wrappers_and_multiline_paths_are_refused(self):
        with self.assertRaises(ValueError):
            renderer.render("ExecStart=/bin/sh wrapper.sh\n", "gameuser", "/opt/fruity", "/srv/content", "AMHE1", None)
        with self.assertRaises(ValueError):
            renderer.render("ExecStart=/old/FruityPrime -server\n", "gameuser", "/opt/fruity", "/srv/data\nExecStart=bad", "AMHE1", None)

    def test_remote_shell_quoting_preserves_literal_arguments(self):
        script = (ROOT / "deploy-server.sh").read_text()
        function = next(line for line in script.splitlines() if line.startswith("shell_quote()"))
        for value in ["plain", "/a path/with spaces", "apostrophe's", "$(printf changed); %s", "a\nb"]:
            command = function + '\nquoted=$(shell_quote "$1"); eval "set -- $quoted"; printf "%s" "$1"'
            result = subprocess.run(["bash", "-c", command, "check", value], check=True, text=True, capture_output=True)
            self.assertEqual(result.stdout, value)


if __name__ == "__main__":
    unittest.main()
