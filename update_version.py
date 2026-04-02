import os
import re
import sys
import xml.etree.ElementTree as ET

path = 'listenarr.api/Listenarr.Api.csproj'
new = os.environ.get('NEW_VERSION')
if not new:
    print("Error: NEW_VERSION environment variable is not set or empty.", file=sys.stderr)
    sys.exit(1)

VERSION_RE = re.compile(r'^\d+\.\d+\.\d+(\.\d+)?$')
if not VERSION_RE.match(new):
    print(f"Error: NEW_VERSION '{new}' is not a valid version format (expected MAJOR.MINOR.PATCH[.REVISION]).", file=sys.stderr)
    sys.exit(1)

tree = ET.parse(path)
root = tree.getroot()

found_version = False
for elem in root.findall('.//Version'):
    elem.text = new
    found_version = True
    break
if not found_version:
    pg = root.find('PropertyGroup')
    if pg is None:
        pg = ET.SubElement(root, 'PropertyGroup')
    ET.SubElement(pg, 'Version').text = new

found_assembly = False
for elem in root.findall('.//AssemblyVersion'):
    elem.text = new
    found_assembly = True
    break
if not found_assembly:
    pg = root.find('PropertyGroup')
    if pg is None:
        pg = ET.SubElement(root, 'PropertyGroup')
    ET.SubElement(pg, 'AssemblyVersion').text = new

try:
    tree.write(path, encoding='utf-8', xml_declaration=True)
    print('Wrote new version and assembly version to csproj')
except OSError as exc:
    print(f"Error: Failed to write '{path}': {exc}", file=sys.stderr)
    sys.exit(1)
