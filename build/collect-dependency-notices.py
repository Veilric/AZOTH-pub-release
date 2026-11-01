"""Collect restored NuGet metadata and textual notices, without dependency binaries.

Usage: python3 build/collect-dependency-notices.py --package-root PATH
The cache inventory is evidence, not a target-specific shipped-dependency SBOM.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET


def collect(package_root, destination):
    packages = []
    project_root = Path(__file__).resolve().parents[1]
    references = {}
    for project in sorted(project_root.rglob('*.csproj')):
        if any(part in ('obj', 'bin', '.git') for part in project.relative_to(project_root).parts):
            continue
        for reference in ET.parse(project).getroot().iter('PackageReference'):
            identity = reference.get('Include')
            if identity:
                references.setdefault(identity.lower(), []).append(project.relative_to(project_root).as_posix())
    for spec in sorted(package_root.glob('*/*/*.nuspec')):
        metadata = ET.parse(spec).getroot().find('{*}metadata')
        if metadata is None:
            continue
        def value(name):
            return metadata.findtext('{*}' + name)
        identity, version = value('id'), value('version')
        if not identity or not version:
            continue
        license_node = metadata.find('{*}license')
        repository = metadata.find('{*}repository')
        notices = []
        declared_file = license_node.text if license_node is not None and license_node.get('type') == 'file' else None
        for source in sorted(spec.parent.rglob('*')):
            if not source.is_file():
                continue
            name = source.name.lower()
            relative = source.relative_to(spec.parent)
            if not (name.startswith(('license', 'licence', 'copying', 'notice', 'ofl')) or relative.as_posix() == declared_file):
                continue
            try:
                source.read_text(encoding='utf-8-sig')
            except (UnicodeDecodeError, OSError):
                continue
            target = destination / 'nuget' / identity / version / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
            notices.append({'file': target.relative_to(destination).as_posix(), 'sha256': hashlib.sha256(source.read_bytes()).hexdigest()})
        packages.append({
            'id': identity, 'version': version,
            'directReferenceProjectsById': references.get(identity.lower(), []),
            'license': {'type': license_node.get('type'), 'value': license_node.text} if license_node is not None else None,
            'licenseUrl': value('licenseUrl'), 'authors': value('authors'), 'copyright': value('copyright'),
            'projectUrl': value('projectUrl'), 'repository': dict(repository.attrib) if repository is not None else None,
            'notices': notices,
        })
    destination.mkdir(parents=True, exist_ok=True)
    (destination / 'nuget-manifest.json').write_text(json.dumps({
        'scope': 'Restored cache metadata, including direct/transitive/build/test packages and possibly unused versions; not a shipped runtime closure.',
        'missingLicensePolicy': 'Unknown; never default to MIT. Native payload licenses may differ from package metadata.',
        'packages': packages,
    }, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print(f'Collected {len(packages)} package records and {sum(len(p["notices"]) for p in packages)} textual notices.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package-root', required=True, type=Path)
    parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'licenses')
    args = parser.parse_args()
    collect(args.package_root, args.output)
