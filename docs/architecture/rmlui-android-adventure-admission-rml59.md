# Android native Adventure admission

The opt-in Android native launcher reuses the authored Adventure slot selection,
resume and New Run confirmation plus the existing native gunship presenter and
campaign backend. It carries no mutable StorySave or second simulation owner.
Copy/Archive use a separate explicit capability when the existing UI save owner
is connected. The prior configured-local and Adventure-only capabilities retain
their restrictions.

One immutable tagged local request travels through the original
`RmlUiAndroidLauncherAdmission`: configured Offline, an exact Adventure slot
projection/fingerprint/New Run flag/hunter, or an exact gunship dock/fingerprint
and selected departure plan. The Activity admits the copied request through the
existing StartScreen `RmlUiLaunchAdventure` or `RmlUiLaunchCampaignShip` owner.
It compares the resulting plan to the captured request, then the EGL owner
independently checks the captured save/dock immediately before the existing
AndroidMatch builder. Hash checks do not provide cross-process atomicity.

The existing cyclic GameView retirement, commit, foreground release, Home
acknowledgment and native lease remain the only lifecycle. The original
admission owner now atomically returns the accepted request with its plan,
after successful original native-owner retirement. No second admission state
machine or serializer is introduced. The existing Adventure Scene constructor
now receives the same early lifetime-registration callback as Offline, so a
partial construction remains visible to the original scene retirement owner.

Ship navigation uses the existing CampaignNavigation/CampaignShipTravel and
AdventureLaunch authority, the actual recent committed dock receipt, and the
existing epoch-owned campaign audio session. Back returns to native saves;
a fresh native Home can present the recently committed dock after normal
scene retirement. Pending or failed commits cannot authorize departure.

Async save management preserves the desktop synchronous path. A confirmed
immutable change is single-flight through the existing controller Job/epoch
cancellation queue. Pending status and Cancel remain accessible while new
launch, source, destination and hunter changes are fenced. The Activity checks
the exact current View/session lifetime, foreground, open admission and absence
of another match or commit. StartScreen repeats full fingerprint preflight and
checks cancellation immediately before the existing guarded byte writer. EGL
never waits for UI work. After byte publication starts, cancellation only retires
UI authority and cannot promise rollback. Exact result identity and fresh save
readback are required before a verified async result is displayed.

The new request/admission source fixture passes49 checks against actual immutable
INITIAL58 engine DTOs and the final58 centrally compiled client DTOs. Existing
linked cycle35,350 and capability661 baselines pass; the extended Adventure
capability/native-document/focus fixture passes688 checks. Those use fake
scene/native boundaries. The linked full menu suite retains the synchronous
journeys and passes36 async assertions, including observed new bytes after
post-effect cancellation. The actual stateless Android dispatch
helper passes7 checks with a fake UI queue/admission/writer. Host/runtime pending
focus and actual temporary-byte33 checks pass. Android managed/package builds
are centrally coordinated and pending. No real device, driver, save mutation,
campaign session or audio acceptance is claimed.
