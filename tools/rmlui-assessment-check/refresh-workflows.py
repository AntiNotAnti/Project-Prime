#!/usr/bin/env python3
"""Refresh source hashes and curated local evidence; never accepts release gates."""
import argparse,json,hashlib,subprocess,datetime
from pathlib import Path
r=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--frozen-aggregate', help='Read authority source bytes from this exact Git commit; do not use mutable source.')
parser.add_argument('--candidate-manifest', type=Path, help='Record current post-aggregate candidate file hashes from this reviewed manifest.')
parser.add_argument('--candidate-applied', action='store_true', help='Record the human-authorized application as an unmerged development candidate, never release acceptance.')
arguments=parser.parse_args()
aggregate=arguments.frozen_aggregate
if aggregate:
 if len(aggregate)!=40 or any(c not in '0123456789abcdef' for c in aggregate): raise SystemExit('Supply the complete lowercase immutable aggregate SHA.')
 subprocess.check_call(['git','cat-file','-e',aggregate+'^{commit}'],cwd=r)
candidate=None
if arguments.candidate_manifest:
 specification=json.loads(arguments.candidate_manifest.read_text())
 candidate={'status':'applied-unmerged-development-candidate' if arguments.candidate_applied else 'prepared-outside-checkout-candidate','release_accepted':False,'native_source_fingerprint':specification.get('nativeSourceFingerprint'),'files':[]}
 for path in specification['files']:
  current=r/path
  if not current.is_file(): raise SystemExit('candidate file absent: '+path)
  expected=arguments.candidate_manifest.parent/'files'/path
  candidate['files'].append({'path':path,'working_tree_sha256':hashlib.sha256(current.read_bytes()).hexdigest(),'matches_reviewed_candidate_bytes':expected.is_file() and expected.read_bytes()==current.read_bytes()})
aggregate_tree=subprocess.check_output(['git','show','-s','--format=%T',aggregate],cwd=r,text=True).strip() if aggregate else None
def source_bytes(path):
 return subprocess.check_output(['git','show',aggregate+':'+path],cwd=r) if aggregate else (r/path).read_bytes()
baseline=json.loads((r/'docs/architecture/rmlui-acceptance-baseline-6a24a39d.json').read_text())
core='src/MphRead/Mods/Launcher/Core/';ui='src/MphRead/Mods/Launcher/RmlUi/';gui='src/MphRead/Mods/Launcher/Gui/'
groups={
'home':[core+'LauncherRouter.cs',gui+'Shell.RmlUiLinks.cs',ui+'Host/RmlUiLauncherPages.cs',ui+'Pages/home/home.rml'],
'news':[core+'NewsCatalog.cs',core+'NewsController.cs',core+'NewsEngineBackend.cs',ui+'Pages/news/NewsPagePresenter.cs',ui+'Pages/news/news.rml'],
'play':[gui+'RmlMultiplayerController.cs',gui+'Shell.RmlUiQueue.cs',ui+'Host/RmlUiLauncherPages.cs',ui+'Pages/play/browser.rml',ui+'Pages/play/queue.rml'],
'lobby':[core+'LobbySessionController.cs',core+'NetLobbySessionBackend.cs',ui+'Presenters/RmlUiLobbyBindings.cs',ui+'Presenters/RmlLobbyAdminPresenter.cs',gui+'Shell.RmlUi.cs',ui+'Pages/lobby/lobby.rml'],
'hunters':[core+'HunterSelectionController.cs',core+'LauncherHunterSelectionBackend.cs',ui+'Presenters/RmlHunterSelectionPresenter.cs'],
'social':[core+'SocialController.cs',core+'LauncherSocialBackend.cs',ui+'Pages/social/SocialPagePresenter.cs',gui+'Shell.RmlUiSocial.cs'],
'settings':[core+'SettingsController.cs',ui+'Settings/EngineSettingsBackend.cs',ui+'Settings/SettingsPagePresenter.cs',ui+'Settings/SettingsPagePresenter.Tools.cs',ui+'Pages/settings/settings.rml',ui+'Pages/settings/settings.rcss'],
'setup':[core+'SetupController.cs',ui+'Pages/setup/EngineSetupBackend.cs',ui+'Pages/setup/SetupPagePresenter.cs'],
'license':[core+'LicenseController.cs',core+'LauncherLicenseBackend.cs',ui+'Pages/license/LicensePagePresenter.cs'],
'community':[core+'CommunityController.cs',core+'CommunityEngineBackend.cs',ui+'Pages/community/CommunityPagePresenter.cs'],
'offline':[core+'OfflineController.cs',ui+'Pages/offline/OfflinePagePresenter.cs',gui+'Shell.RmlUiPages.cs'],
'adventure':[core+'AdventureController.cs',ui+'Pages/adventure/AdventurePagePresenter.cs'],
'theatre':[core+'TheatreController.cs',core+'TheatreEngineBackend.cs',core+'TheatrePlaybackController.cs',core+'TheatreViewportController.cs',ui+'Pages/theatre/TheatrePagePresenter.cs',ui+'Pages/theatre/TheatrePlaybackPagePresenter.cs'],
'studio':[core+'StudioController.cs',core+'StudioEntryBackend.cs',ui+'Pages/studio/StudioPagePresenter.cs'],
'ingame':[core+'InGameController.cs',ui+'Pages/ingame/InGamePagePresenter.cs'],
'results':[core+'MatchResultsController.cs',core+'AimResultsController.cs',ui+'Pages/ingame/MatchResultsPagePresenter.cs',ui+'Pages/ingame/AimResultsPagePresenter.cs'],
'hud':[core+'HudEditorController.cs',ui+'Pages/hud/HudEditorPagePresenter.cs',ui+'Settings/EngineSettingsBackend.Hud.cs'],
'input':[ui+'Host/RmlUiInput.cs',ui+'Host/RmlUiDesktopInput.cs',ui+'Host/RmlUiWindowsIme.cs',ui+'Host/RmlUiCocoaIme.cs',ui+'Host/RmlUiLinuxIme.cs',ui+'Host/RmlUiIbusApi.cs',ui+'Components/RmlUiAccessibility.cs',ui+'Components/RmlUiCocoaAccessibility.cs',ui+'Components/RmlUiWindowsAccessibility.cs','src/MphRead.Android/AndroidRmlUiInput.cs','src/MphRead.Android/AndroidRmlUiAccessibility.cs'],
'quit':[gui+'Shell.RmlUiPages.cs',core+'InGameController.cs']}
counts={'news':{'contracts':28,'native':348},'offline':{'contracts':50,'native':555},'adventure':{'contracts':31,'native':132},'community':{'contracts':129,'native':488},'theatre':{'contracts':36,'native':266},'hud':{'contracts':33,'native':376},'social':{'contracts':149,'native':343},'studio':{'contracts':136,'native':152,'combined':288},'settings':{'contracts':23,'authoritative_engine':29,'native':40,'compact_layout':765},'setup':{'contracts':27,'native_engine_monitor':34},'ingame':{'contracts':46,'native_results_combined':155},'results':{'combined_with':'ingame'},'play':{'injected_multiplayer_contracts':72,'real_server_queue_regressions':9,'actual_waitlist':310,'rendered_multiplayer_live_coverage':'user-owned and unperformed by this harness'},'lobby':{'lobby_hunter_contracts':92,'native_admin_hunter':67,'fake_lifecycle_cycles':50,'real_native_udp_checks':879,'real_udp_lobby_cycles':50,'same_controllers_match_returns':20,'rendered_gpu_live_coverage':'user-owned;879-check fixture uses explicit empty scene bootstrap'},'hunters':{'combined_with':'lobby'},'license':{'contracts':43,'native':60,'production_account_operations':0},'home':{'router_contracts':75,'shared_theme_native':610,'native_only_shared_shell_routing':645,'transitional_shared_shell_routing':645,'native_home_candidate_captures':16,'representative_candidate_captures':4},'input':{'platform_provider_contracts':150,'native_semantic':150,'cocoa_accessibility':218,'cocoa_ime':222,'linux_ime_native_fake_bus':219,'linux_ime_hosted_real_ibus':233,'android_inputconnection':13,'android_provider_fixture':16}}
groups['input'] += [str(path.relative_to(r)) for path in (r/ui/'Components').glob('RmlUiLinuxAccessibility*.cs')]
workflow=[];sources=set()
fixture_barrier_in_aggregate=bool(aggregate and b'await WaitStableProjection("initial Account semantic projection")' in source_bytes('src/MphRead.Android/AndroidRmlUiAccessibilityCheck.cs'))
for w in baseline['workflows']:
 id=w['id'];prefix=id.split('.')[0];g=prefix
 if id=='home.news':g='news'
 elif id=='update.versions':g='setup'
 elif id=='app.quit':g='quit'
 elif id=='offline.rematch':g='results'
 elif id.startswith('replay.'):g='studio'if id=='replay.studio'else'theatre'
 elif id.startswith('creation.'):g='hud'if id=='creation.hud'else'studio'
 elif id=='ingame.results':g='results'
 ps=groups[g]
 for p in ps:
  try: source_bytes(p)
  except (FileNotFoundError,subprocess.CalledProcessError): raise SystemExit('missing mapped source '+p)
  sources.add(p)
 gates=['Integrated success/failure/cancel/persistence/cleanup scenarios at an immutable review head.','Required automated golden/keyboard/gamepad/pointer/platform acceptance; external physical live coverage is user-owned.']
 if g in ('lobby','play','social','ingame','results'):gates.append('Automated native DOM/server/custom-map/queue/recovery and match-return invariants at the exact review head; rendered multiplayer live coverage is user-owned.')
 if g=='community':gates.append('Authorized staging-account service/publication and real package/library/scene cycles; isolated authority boundaries are fake.')
 if g=='license':gates.append('Automated account recovery/linking/verification and service failures; live secure input coverage is user-owned.')
 if g=='setup':gates.append('Successful user ROM extraction, update installation/rollback and platform picker/lifecycle acceptance were deliberately not executed.')
 if g=='input':gates+=['Windows UIA and Linux AT-SPI source/native/GLib contracts exist; external actual OS fixtures await hosted CI.','Automated local/CI IME/provider and shifted punctuation/Wayland acceptance; physical vendor/screen-reader runs are user-owned coverage.','Six-language shared chrome is localized; per-route body translations remain open.']
 if g=='studio':gates.append('Paired Studio process/picker/IPC acceptance; conditional Studio workspace RmlUi migration is not completed.')
 workflow.append({'id':id,'slice':w['slice'],'legacy_behavior':w['behavior'],'legacy_authority':w['authority'],'implementation_state':'source-implemented','acceptance_status':'partial','native_sources':ps,'local_evidence_group':g,'remaining_gates':gates})
manifest=[{'path':p,'sha256':hashlib.sha256(source_bytes(p)).hexdigest()}for p in sorted(sources)]
out={'schema_version':1,'assessment_kind':'immutable-aggregate-source-with-checkpoint-specific-evidence' if aggregate else 'mutable-working-tree-source-and-local-evidence','aggregate_source_sha':aggregate,'aggregate_tree_sha':aggregate_tree,'captured_at_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'observed_git_head':subprocess.check_output(['git','rev-parse','HEAD'],cwd=r,text=True).strip(),'frozen_source_sha':baseline['source_sha'],'immutable_baseline':'rmlui-acceptance-baseline-6a24a39d.json','workflow_count':len(workflow),'slices':[{'id':item['id'],'title':item['title'],'required_for':item['required_for'],'acceptance':item['acceptance'],'acceptance_status':'not-started' if item['id'].startswith('STUDIO-') else 'partial','completed':False} for item in baseline['slices']],'release_parity_accepted':False,'current_user_acceptance_scope':{'user_owns_live_testing':True,'external_physical_and_screen_reader_runs_block_completion':False,'rendered_multiplayer_live_coverage_user_owned':True,'required_automated_environments':['local Mac','hosted CI/software GPU','Android emulator'],'coverage_policy':'Do not claim unperformed physical/screen-reader/vendor-device runs; user-owned live coverage is not an implementation or default/removal blocker.'},'evidence_policy':'Counts identify named local source/native harnesses and retain fake authority boundaries. Source hashes capture this audit, not an immutable test-head certificate. Canceled, stale or skipped CI is never passing evidence. Required automated integration gates and user-owned physical coverage remain distinct.','local_evidence_groups':counts,'workflows':workflow,'source_manifest':manifest,'user_owned_live_coverage':['External physical device/vendor IME/VoiceOver/TalkBack/NVDA/Narrator/Orca','Rendered multiplayer/multiple real game windows/gameplay ergonomics; not exercised by the explicit empty-scene DOM/UDP fixture'],'post_aggregate_candidate':candidate,'fixture_projection_barrier_in_aggregate':fixture_barrier_in_aggregate,'post_aggregate_fixture_changes':'None recorded' if fixture_barrier_in_aggregate else 'Android CHECK initial-focus exact owner/provider projection barrier is a later fixture change; final54 actual retry pending, production revision guards unchanged.','conditional_studio_status':'game-client entry implemented; independent Studio retains its toolkit under the conditional track; standalone authoring migration is not claimed' ,'open_release_gates':['Windows/Linux production runtime/packaging/clean-install/signing/CI matrix','Exact-head automated native DOM/UDP/server 2/4/8-player, custom-map, queue/admin/ready and match-return/recovery checks; rendered gameplay live coverage user-owned','Automated emulator safe-area/IME/controller/background/surface/device recovery; physical vendor-device runs are user-owned coverage','Complete legacy-vs-native golden/hit traces and same-machine cold/idle/p95/resource measurements','Per-route six-language body localization and automated platform-provider acceptance; physical screen readers are user-owned coverage','Accepted default/canary rollout, client dependency removal and conditional Studio acceptance']}
(r/'docs/architecture/rmlui-current-workflow-assessment.json').write_text(json.dumps(out,indent=2,ensure_ascii=False)+'\n')
print('mapped',len(workflow),'workflows;',len(manifest),'exact authority sources;',aggregate or 'working tree')
