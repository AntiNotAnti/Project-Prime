#!/usr/bin/env python3
"""Produce a review proposal from file names only; never changes Git state."""
import datetime
import json
import subprocess
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'docs/architecture/rmlui-review-slice-proposal.json'

def git(*arguments):
    return subprocess.check_output(['git', *arguments], cwd=ROOT, text=True).splitlines()

modified = set(git('diff', 'HEAD', '--name-only'))
untracked = set(git('ls-files', '--others', '--exclude-standard'))
files = sorted(modified | untracked | {str(OUT.relative_to(ROOT))})
GUI = 'src/MphRead/Mods/Launcher/Gui/'
CORE = 'src/MphRead/Mods/Launcher/Core/'
UI = 'src/MphRead/Mods/Launcher/RmlUi/'
excluded = []
assignments = []

for path in files:
    # Parent-owned all-route convergence is staged only after its route dependencies.
    if path.startswith(GUI + 'Shell.RmlUi') or path == GUI + 'Shell.cs':
        excluded.append({'path': path, 'integration_slice': 'RML-18',
            'reason': 'Parent-owned all-route Shell integration excluded from per-route file ownership.'})
        continue
    slice_id, rationale, shared = None, '', []
    if path == '.github/workflows/build.yml':
        slice_id, rationale = 'RML-16', 'Native/platform CI and package gates.'
        shared = ['RML-00 baseline gates', 'RML-03 IME/provider fixtures', 'RML-17 Android runtime gates', 'RML-20 native-only/server boundaries: split chronological hunks.']
    elif path.startswith('docs/architecture/'):
        slice_id = 'RML-18'
        if path.endswith('rmlui-runtime-boundaries.md'): slice_id = 'RML-01'; shared = ['Later route/platform evidence belongs in RML-18; do not backdate counts.']
        elif path.endswith('rmlui-platform-text-input.md') or path.endswith('rmlui-theme-accessibility.md'): slice_id = 'RML-03'
        rationale = 'Architecture/evidence documentation; frozen baseline history is retained.'
    elif path.startswith('native/rmlui-poc/'):
        slice_id, rationale = 'RML-01', 'Shared native ABI/framework; all bounded future typed action IDs can land in foundation.'
        shared = ['Current native cpp/API/menu also integrate RML-03 semantic/text protocols and RML-05..14 actions; stage matching headers together, or split additive exports by dependent slice.']
    elif path.startswith('src/MphRead.Android/'):
        slice_id, rationale = 'RML-17', 'Native Android surface, input/lifecycle and shared presenter integration.'
        if '/Native/' in path or path.endswith('AndroidRmlUi.NoAvalonia.targets'):
            slice_id, rationale = 'RML-20', 'Android UI-assembly-free entry/source boundary.'
        elif path.endswith('MphRead.Android.csproj'):
            slice_id = 'RML-16'; shared = ['RML-17 opt-in runtime/presenter assets', 'RML-20 Avalonia-free source/package cutover: split hunks and retain platform feature guards.']
        elif path.endswith('MainActivity.SettingsArchive.cs'):
            slice_id = 'RML-08'; shared = ['Android boundary guards depend on RML-17/20.']
        elif path.endswith('AndroidRmlUiAccessibility.cs') or path.endswith('AndroidRmlUiAccessibilityCheck.cs'):
            shared = ['Depends on RML-03 immutable semantic service and RML-09 real Account fixture.', 'UI/provider callbacks must match the Func<Command,bool> API.']
        elif path.endswith('AndroidRmlUiSession.cs') or path.endswith('GameView.RmlUi.cs') or path.endswith('AndroidRmlUiView.cs'):
            shared = ['Final file consumes RML-05..14 presenters and RML-03 provider/text APIs; stage after those sources.', 'Native-only entry hooks depend on RML-20 or need guarded early hunks.']
    elif path == 'src/MphRead/MphRead.csproj':
        slice_id, rationale = 'RML-16', 'Desktop runtime asset/font/license package integration.'
        shared = ['RML-01 feature flag/build boundary', 'RML-03 fallback font', 'RML-19 shipping policy', 'RML-20 compile/dependency exclusions: whole final file cannot safely be treated as foundation-only.']
    elif path.startswith('src/MphRead/Assets/Fonts/NotoSansJP'):
        slice_id, rationale = 'RML-03', 'Licensed Japanese glyph fallback for native text/chrome.'
        shared = ['RML-16/17 manifests must bundle font and attribution.']
    elif path.startswith(CORE):
        name = Path(path).name
        if name.startswith(('Offline','Adventure')): slice_id = 'RML-11'
        elif name.startswith('Community'): slice_id = 'RML-10'
        elif name.startswith(('License','LauncherLicense')): slice_id = 'RML-09'
        elif name.startswith(('Settings','Setup')): slice_id = 'RML-08'
        elif name.startswith(('Social','LauncherSocial')): slice_id = 'RML-07'
        elif name.startswith(('Hunter','LauncherHunter')): slice_id = 'RML-06'
        elif name.startswith(('InGame','MatchResults','AimResults')): slice_id = 'RML-13'
        elif name.startswith('HudEditor'): slice_id = 'RML-14'
        elif name.startswith(('Theatre','Studio')):
            slice_id = 'RML-12'
            if name.startswith('Studio'): shared = ['Shared independent Studio launch authority also implements RML-14 Map entry; do not duplicate it.']
        elif name.startswith('News') or name == 'LauncherRoute.cs': slice_id = 'RML-02'
        elif name == 'LauncherUiSelectionPolicy.cs': slice_id = 'RML-20'; shared = ['Presentation-neutral selection authority is reused by RML-06; move the neutral policy earlier if Hunter sources require it before client removal.']
        elif name.startswith(('Lobby','NetLobby')): slice_id = 'RML-04'
        rationale = 'Presentation-neutral application/service controller and copied data contract.'
    elif path.startswith(UI):
        part = path[len(UI):]
        if part.startswith('Pages/'):
            area = part.split('/')[1]
            slice_id = {'home':'RML-02','news':'RML-02','play':'RML-05','lobby':'RML-05','hunters':'RML-06','social':'RML-07','settings':'RML-08','setup':'RML-08','license':'RML-09','community':'RML-10','offline':'RML-11','adventure':'RML-11','theatre':'RML-12','studio':'RML-12','ingame':'RML-13','hud':'RML-14'}.get(area)
            if part == 'Pages/lobby/lobby.rml' or part == 'Pages/lobby/lobby.rcss': slice_id = 'RML-04'
            rationale = 'Route-owned authored documents, presenter and local check helpers.'
            if area == 'studio': shared = ['Map launch entry is also RML-14, using the same RML-12 Studio authority.']
        elif part.startswith('Settings/'): slice_id, rationale = 'RML-08', 'Typed Settings schema/persistence/input/presenter and actual authority checks.'
        elif part.startswith('Presenters/'):
            slice_id = 'RML-06' if 'Hunter' in part else 'RML-05' if 'Admin' in part else 'RML-04'
            rationale = 'Native lobby/Hunter/controller bindings.'
        elif part.startswith(('Components/','Themes/')):
            slice_id, rationale = 'RML-02', 'Shared composed chrome and explicit RmlUi control/theme assets.'
            if 'Accessibility' in part or 'VisualPolicy' in part: slice_id = 'RML-03'
            if part.endswith(('.rcss','.rml')): shared = ['Final high-contrast/touch/localized chrome uses RML-03 policy; final footer/Nav exposes future routes, which must stay gated until RML-18 integration.']
        elif part.startswith('Host/'):
            slice_id, rationale = 'RML-01', 'Shared managed host/document/action/framework.'
            if any(fragment in part for fragment in ('WindowsIme','CocoaIme','CocoaViewApi','LinuxIme','IbusApi','LinuxDesktopInput','DesktopInput')): slice_id = 'RML-03'
            elif part.endswith(('RmlUiLauncherPages.cs','RmlUiMenuPages.cs','RmlUiPageManager.cs')): slice_id = 'RML-02'; shared = ['Final launcher composition includes RML-05 queue/advanced page bindings; keep shared core separate from late all-route integration if needed.']
            if part.endswith(('RmlUiNativeBridge.cs','RmlUiIntent.cs','RmlUiHost.cs','RmlUiInput.cs')): shared = ['Final source includes RML-03 text/semantic and RML-05..14 action contracts; foundation may own additive ABI IDs, but matching optional capabilities/exports must land together.']
        elif part.startswith('Render/'): slice_id, rationale = 'RML-15', 'Engine-owned draw-list compositor.'
    elif path.startswith('src/MphRead/Mods/Launcher/Native/') or path == GUI+'Shell.Presentation.cs':
        slice_id, rationale = 'RML-20', 'Native-only application entry and presentation abstraction.'
        shared = ['Final NativeShell consumes all route presenters and RML-19 policy: stage after RML-18/19.']
    elif path.startswith('src/MphRead/Mods/Launcher/Portable/'):
        if path.endswith('LauncherPrefs.cs'): slice_id = 'RML-03'; shared = ['Shared prefs file also serves RML-08 explicit settings rows; preserve legacy keys/values.']
        elif path.endswith('GameFiles.cs'): slice_id = 'RML-08'; shared = ['Toolkit-free/source guards support RML-20; split those hunks if extraction lands earlier.']
        elif path.endswith('LauncherUiPerformance.cs'): slice_id = 'RML-18'
        elif path.endswith('LauncherUiRuntime.cs'): slice_id = 'RML-19'
        elif path.endswith('HunterLicenseClient.cs'): slice_id = 'RML-09'; shared = ['Toolkit-free authority guard is required by native-only RML-20 and Community RML-10; retain one existing auth implementation.']
        rationale = 'Existing portable authority or runtime policy instrumentation.'
    elif path.startswith(GUI):
        name = Path(path).name
        if name in ('LobbyScreen.cs','RmlLobbyRulesEditor.cs'): slice_id = 'RML-04'
        elif name in ('BotManagementView.cs','RmlMultiplayerController.cs'): slice_id = 'RML-05'
        elif name == 'NewsWorkspace.cs' or path.endswith('/NewsWorkspace.cs'): slice_id = 'RML-02'
        elif name in ('StartScreen.cs','Shell/PrimeRouter.cs'): slice_id = 'RML-02'
        elif path.endswith('Shell/PrimeRouter.cs'): slice_id = 'RML-02'
        elif name == 'UiSurface.cs': slice_id = 'RML-20'
        elif name == 'RmlUiPrototype.cs': slice_id = 'RML-01'; shared = ['Final prototype hooks modern composition RML-15, all-route documents RML-18, provider/text RML-03 and UI runtime policy RML-19; split foundation lifecycle from late integration hunks.']
        rationale = 'Legacy/shared presentation adapter changed to reuse native authority.'
        if name in ('StartScreen.cs','LobbyScreen.cs'): shared = ['Whole file mixes RML-02 navigation, RML-04 authority and RML-20 source guards: split hunks or stage after prerequisites.']
    elif path == 'src/MphRead/Mods/Network/LobbyQueueClient.cs': slice_id, rationale = 'RML-05', 'Existing queue authority cancellation/Bye cleanup fix.'
    elif path == 'src/MphRead/Mods/ThumbnailBatch.cs': slice_id, rationale = 'RML-12', 'Existing preview-worker authority retirement race fix discovered by integrated client runs.'; shared = ['RML-18 capture/performance guards exercise this shared authority; retain serialized process shutdown without backdating validation.']
    elif path == 'src/MphRead/Mods/Network/DemoPlayback.cs': slice_id, rationale = 'RML-12', 'Existing replay camera/playback authority integration.'
    elif path == 'src/MphRead/Mods/PauseMenu.cs': slice_id, rationale = 'RML-13', 'Native in-game menu handoff.'; shared = ['RML-20 toolkit guard/source boundary hunks.']
    elif path.startswith('src/MphRead/Mods/Render/') or path == 'src/MphRead/Renderer.cs':
        slice_id, rationale = 'RML-15', 'Modern renderer/chamber/shader/composition changes.'
        shared = ['Engine renderer/UiOverlay final files also include RML-13 running-match and RML-18 all-route integration plus RML-20 source guards.']
    elif path.startswith(('src/MphRead/Mods/StudioIntegration/','src/MphRead/Mods/StudioRendering/')): slice_id, rationale = 'RML-12', 'Existing Studio authority/viewport broker changes.'; shared = ['RML-14 creation entry and RML-20 native-only guards.']
    elif path == 'src/MphRead/Mods/ModEntry.cs': slice_id, rationale = 'RML-20', 'Client entry/source boundary.'; shared = ['RML-19 default/rollback policy.']
    elif path.startswith(('src/MphRead/Mods/Diagnostics/','src/MphRead/Mods/Input/','src/MphRead/Mods/Training/')): slice_id, rationale = 'RML-20', 'Diagnostics/gamepad/training assembly/source guards.'; shared = ['Review actual hunks for RML-03/11 behavior changes, rather than classifying a whole diagnostic file solely by its name.']
    elif path == 'tools/asset-guard-allow.txt': slice_id, rationale = 'RML-03', 'Licensed Noto Sans JP font guard allow entry.'; shared = ['RML-16/17 packages must preserve the matching OFL/source attribution.']
    elif path.startswith('tools/'):
        folder = path.split('/')[1]
        names = {'launcher-router-check':'RML-02','lobby-controller-check':'RML-04','rmlui-lobby-page-check':'RML-05','rmlui-multiplayer-check':'RML-05','rmlui-lobby-live-check':'RML-18','rmlui-shell-routing-check':'RML-18','license-controller-check':'RML-09','rmlui-account-diagnostic-check':'RML-09','news-controller-check':'RML-02','offline-controller-check':'RML-11','adventure-controller-check':'RML-11','community-controller-check':'RML-10','theatre-controller-check':'RML-12','rmlui-studio-check':'RML-12','hud-controller-check':'RML-14','in-game-controller-check':'RML-13','rmlui-settings-check':'RML-08','rmlui-settings-layout-check':'RML-08','rmlui-settings-engine-check':'RML-08','rmlui-setup-check':'RML-08','rmlui-setup-engine-check':'RML-08','rmlui-page-check':'RML-02','rmlui-core-check':'RML-01','rmlui-runtime-check':'RML-01','rmlui-compositor-check':'RML-15','modern-shaders':'RML-15','rmlui-accessibility-check':'RML-03','rmlui-cocoa-accessibility-check':'RML-03','rmlui-cocoa-check':'RML-03','rmlui-linux-ime-check':'RML-03','rmlui-platform-accessibility-check':'RML-03','native-client-check':'RML-20','launcher-ui-policy-check':'RML-19','rmlui-assessment-check':'RML-18','rmlui-host-profile':'RML-18','rmlui-drawlist-cache-check':'RML-15','rmlui-es-shader-check':'RML-15'}
        slice_id = names.get(folder)
        if folder == 'rmlui': slice_id = 'RML-18' if path.endswith(('performance-check.py','golden-capture.py','GOLDEN_CAPTURE.md')) else 'RML-20' if path.endswith('verify-android-client.py') else 'RML-16'
        rationale = 'Named contract/native/platform evidence tool matching its implementation slice.'
        if folder in ('rmlui-core-check','rmlui-runtime-check'): shared = ['Final tool asserts future RML-03/05..14 contracts; foundation test can own centralized action IDs but optional page assertions require their authored assets.']
    assignments.append({'path': path, 'change_kind': 'modified' if path in modified else 'untracked',
        'proposed_slice': slice_id or 'MANUAL-REVIEW', 'rationale': rationale or 'No safe file-name assignment; inspect the diff.',
        'shared_or_late_dependencies': shared, 'whole_file_ready_for_early_slice': not shared and slice_id is not None})

out = {'schema_version': 1, 'proposal_only': True, 'captured_at_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'observed_git_head': git('rev-parse','HEAD')[0],
    'scope': 'Current diff versus HEAD (staged and unstaged) and nonignored untracked files only; not a reconstruction of commits already present in HEAD.',
    'git_mutations_performed': False, 'file_count': len(files), 'assigned_file_count': len(assignments),
    'excluded_parent_integration': excluded, 'counts_by_proposed_slice': dict(sorted(Counter(row['proposed_slice'] for row in assignments).items())),
    'review_order': ['RML-00','RML-01','RML-02','RML-03','RML-04','RML-05','RML-06','RML-07','RML-08','RML-09','RML-10','RML-11','RML-12','RML-13','RML-14','RML-15','RML-16','RML-17','RML-18','RML-19','RML-20'],
    'limitations': ['A file-name proposal is not an independently compiling commit certificate; shared entries need chronological hunk partitioning.',
        'Already committed foundation/social work is absent from this current-diff-only inventory and must be retained/reviewed in its existing stack.',
        'Route controllers/backends/presenters/assets/tools should land together; all-route Shell convergence is explicitly deferred to RML-18.',
        'Do not move final shipping defaults or Avalonia removals into early foundation/package slices; RML-19/20 retain separate release gates.',
        'Standalone Studio STUDIO-RML-00..05 is conditional and not implemented by the client entry slices.'],
    'files': assignments}
OUT.write_text(json.dumps(out,indent=2)+'\n')
print(f'Proposed {len(assignments)} files; excluded {len(excluded)} parent integration files; shared/late files {sum(bool(row["shared_or_late_dependencies"]) for row in assignments)}.')
print(json.dumps(out['counts_by_proposed_slice'],sort_keys=True))
