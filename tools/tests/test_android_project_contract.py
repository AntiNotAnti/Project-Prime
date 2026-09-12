"""Portable Node launcher dependencies must remain explicitly linked into Android."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


class AndroidNodeProjectContractTests(unittest.TestCase):
    def test_portable_node_launcher_and_its_contracts_are_included(self):
        project = ET.parse(ROOT / 'src/Android/Android.csproj').getroot()
        references = {item.attrib['Include'] for item in project.iter('ProjectReference')}
        self.assertIn('../Server.Shared/Server.Shared.csproj', references)
        self.assertNotIn('../Server.Worker/Server.Worker.csproj', references)
        self.assertNotIn('../Server.Node/Server.Node.csproj', references)
        compiled = {item.attrib['Include'] for item in project.iter('Compile')}
        for source in ('Accounts/AccountSession.Nodes.cs', 'Networking/NodeHealthCache.cs',
                       'Networking/Nodes/NodeControlClient.cs', 'Launcher/Gui/NodeBrowserView.cs'):
            self.assertIn('../Client/' + source, compiled)
            self.assertTrue((ROOT / 'src/Client' / source).is_file())


if __name__ == '__main__':
    unittest.main()
