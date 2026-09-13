"""Android consumes portable client code through normal project references."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


class AndroidNodeProjectContractTests(unittest.TestCase):
    def test_portable_node_launcher_and_its_contracts_are_included(self):
        project = ET.parse(ROOT / 'src/Android/Android.csproj').getroot()
        references = {item.attrib['Include'] for item in project.iter('ProjectReference')}
        self.assertIn('../Client.Core/Client.Core.csproj', references)
        self.assertIn('../Client.Presentation/Client.Presentation.csproj', references)
        self.assertIn('../Server.Shared/Server.Shared.csproj', references)
        self.assertNotIn('../Server.Worker/Server.Worker.csproj', references)
        self.assertNotIn('../Server.Node/Server.Node.csproj', references)

        compiled = {item.attrib['Include'].replace('\\', '/')
                    for item in project.iter('Compile')}
        self.assertFalse(any(source.startswith('../Client/') for source in compiled))
        self.assertFalse(any(source.startswith('../Renderer/') for source in compiled))

        core_sources = (
            'Accounts/AccountSession.Nodes.cs',
            'Networking/NodeHealthCache.cs',
            'Networking/Nodes/NodeControlClient.cs',
        )
        for source in core_sources:
            self.assertTrue((ROOT / 'src/Client.Core' / source).is_file())
        self.assertTrue((ROOT / 'src/Client.Presentation/Launcher/Gui/NodeBrowserView.cs').is_file())

        presentation = ET.parse(
            ROOT / 'src/Client.Presentation/Client.Presentation.csproj').getroot()
        linked_items = {
            item.attrib['Include'].replace('\\', '/')
            for item in presentation.iter()
            if item.tag in {'Compile', 'AvaloniaXaml', 'AvaloniaResource', 'EmbeddedResource'}
            and 'Include' in item.attrib
        }
        self.assertFalse(any(source.startswith('../Client/') for source in linked_items))

        presentation_reference = next(item for item in project.iter('ProjectReference')
            if item.attrib['Include'] == '../Client.Presentation/Client.Presentation.csproj')
        properties = {
            value.strip() for value in
            presentation_reference.attrib.get('AdditionalProperties', '').split(';')
            if value.strip()
        }
        self.assertIn('PrimeEnableAndroidPresentation=true', properties)
        self.assertIn(
            'BaseIntermediateOutputPath=$(AndroidPresentationIntermediatePath)', properties)
        self.assertIn(
            'MSBuildProjectExtensionsPath=$(AndroidPresentationIntermediatePath)', properties)


if __name__ == '__main__':
    unittest.main()
