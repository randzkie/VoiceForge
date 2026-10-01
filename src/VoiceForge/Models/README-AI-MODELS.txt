VOICEFORGE - AI VOICES (RVC) MODEL GUIDE
========================================

This folder is the second place VoiceForge looks for AI voice models. The
first (recommended) place is:

    %AppData%\VoiceForge\models

Open it from the app: AI Voices page -> "OPEN MODELS FOLDER" button.

The AI Voices feature runs RVC (Retrieval-based Voice Conversion) models
entirely on your PC via ONNX Runtime - no Python, no cloud. You need to
place model files here yourself; VoiceForge cannot ship them because good
voice models are large (20-100 MB) and usually licensed per creator.


WHAT TO PUT IN THE FOLDER
-------------------------

1) A feature extractor (REQUIRED - shared by all voices)
   ContentVec / HuBERT ONNX, 16 kHz waveform in. Without it no AI
   voice can load. Download one from the links below and drop it in
   this folder. Renaming to "hubert.onnx" is OPTIONAL - any file whose
   name contains hubert / contentvec / content-encoder or starts with
   "vec-" is auto-detected (e.g. vec-256-layer-9.onnx works as-is).

2) <VoiceName>.onnx                   (one file per voice)
   An RVC generator exported to ONNX. The official RVC export
   (5 inputs: phone, phone_lengths, pitch, pitchf, ds) and the
   7-input flavor (..., sid) both load without any editing - the
   tensor names are auto-resolved at load time.

3) <VoiceName>.json                   (OPTIONAL sidecar)
   Tensor names + geometry. VoiceForge creates one automatically from
   sidecar-template.json defaults the first time a voice loads - only
   edit it if your export uses non-standard tensor names (check in
   Netron: https://netron.app) or a different sample rate / hop.


QUICK DOWNLOAD LINKS (verified live October 2026)
-------------------------------------------------

READY-TO-USE VOICES (ONNX, no conversion needed):
  * ozada/onnx_rvc  (Hugging Face)
    https://huggingface.co/ozada/onnx_rvc/tree/main
    Files: woman_1.onnx ... woman_4.onnx, beyonce.onnx, drake.onnx,
    sabrina.onnx, lana.onnx, donaldtrump.onnx, travisscottv2.onnx
    PLUS the shared feature extractors in the same repo:
      vec-256-layer-9.onnx  <- download this one
      vec-768-layer-12.onnx
    Direct file links (use the "download" arrow on the page, or):
      https://huggingface.co/ozada/onnx_rvc/resolve/main/woman_1.onnx
      https://huggingface.co/ozada/onnx_rvc/resolve/main/vec-256-layer-9.onnx
    SETUP: download one voice + one extractor, put both in this
    folder. Renaming the extractor to hubert.onnx is optional.
    Extractor pairing: try vec-256-layer-9.onnx first; if loading
    fails with a shape/size error on 'phone', the voice wants the
    768-dim extractor (vec-768-layer-12.onnx) instead.
    NOTE: these are third-party community voices - respect the
    original artists; for personal/fun use only, no impersonation.

PURE-ONNX BASE MODELS (feature extractor + pitch, no voices):
  * TigreGotico/voiceclonnx-rvc  (ContentVec-768 + RMVPE, int8 too)
    https://huggingface.co/TigreGotico/voiceclonnx-rvc/tree/main
    contentvec_768l12.onnx (rename to hubert.onnx) + rmvpe.onnx
  * ohnoitsaninja/rvc-base-onnx  (HuBERT layer12 + RMVPE)
    https://huggingface.co/ohnoitsaninja/rvc-base-onnx/tree/main

OFFICIAL RVC PROJECT (checkpoints, .pth voices, export tools):
  * Model hub:  https://huggingface.co/lj1995/VoiceConversionWebUI
      hubert_base.pt  -> convert to hubert.onnx (snippet below)
      rmvpe.onnx      (official RMVPE, ONNX format)
      pretrained_v2/  (RVC base voices in .pth)
  * Code/export scripts:
      https://github.com/RVC-Project/Retrieval-based-Voice-Conversion-WebUI

FREE VOICES IN .PTH FORMAT (convert once, then keep forever):
  * wok000/vcclient_model  (Amitaro, Kikoto Kurage/Mahiro, Tokina
    Shigure, Tsukuyomi-chan - permissive terms of use, Japanese)
    https://huggingface.co/wok000/vcclient_model/tree/main/rvc
  * DarkWeBareBears69/My-RVC-Voice-Models (community .zip packs)
    https://huggingface.co/DarkWeBareBears69/My-RVC-Voice-Models
  * AI Hub community index: https://docs.aihub.gg
  * NOTE: weights.gg shut down (April 2026); its community moved to
    HF collections like the ones above and sites such as nicevois.com.

PTH -> ONNX CONVERTERS:
  * In-browser (no install): https://voicechanger.live
  * RVC WebUI "Export ONNX", or the Python snippet below.
  After converting, also fetch/rename a feature extractor as
  hubert.onnx (the voice .onnx alone is not enough).


WHERE TO GET READY-MADE RVC .ONNX VOICES (details)
--------------------------------------------------

* The ozada/onnx_rvc repo above is the fastest path: voices are
  already ONNX and load here directly.
* RVC WebUI / RVC-GUI: train or download .pth weights, then use the
  "Export ONNX" feature (or the export snippet below).
* The w-okada VC-Client model packs often contain both hubert and
  voice .onnx files that load here directly.

 CONVERTING .PTH TO .ONNX (one-time, needs Python)
 -------------------------------------------------
 Inside a working RVC installation (rvc-python or Retrieval-based-
 Voice-Conversion-WebUI):

     import torch, fairseq, json
     from lib.rvc.models import SynthesizerTrnMs256NSFsid  # v2 40k
     # ...load your .pth checkpoint, then use the repo's
     # tools/onnx/export script; it writes model.onnx with the
     # standard tensor names VoiceForge expects by default.

 CONVERTING hubert_base.pt TO hubert.onnx
 ----------------------------------------
     import torch
     from fairseq import checkpoint_utils
     models, cfg, task = checkpoint_utils.load_model_ensemble_and_task(
         ["hubert_base.pt"])
     model = models[0]
     class Wrapper(torch.nn.Module):
         def __init__(self, m): super().__init__(); self.m = m
         def forward(self, x):
             r = self.m.extract_features(x)[0]
             return r[:, :256] if r.shape[-1] > 256 else r
     torch.onnx.export(Wrapper(model).eval(), (torch.zeros(1, 16000),),
         "hubert.onnx", input_names=["source"], output_names=["embed"],
         dynamic_axes={"source": {1: "samples"}, "embed": {1: "frames"}},
         opset_version=13)


LATENCY EXPECTATIONS (CPU)
--------------------------
Chunk size 250 ms -> snappy but needs a strong CPU.
Chunk size 500 ms -> recommended; ~0.5 s extra delay in AI mode.
Chunk size 1000 ms -> most stable on older CPUs.

HuBERT is the expensive half. If audio gaps appear (the status line
will keep showing drops), switch to a bigger chunk. DSP presets keep
working independently and stay at ~70 ms latency.


TROUBLESHOOTING
---------------
* "AI feature extractor not found" -> no extractor .onnx is in the
  search folders. Download vec-256-layer-9.onnx (links above) into
  %AppData%\VoiceForge\models (open it via OPEN MODELS FOLDER), then
  press RESCAN. Renaming to hubert.onnx is optional.
  Windows trap: "hide file extensions" can turn a rename into
  hubert.onnx.onnx - it still works (name contains hubert), but the
  file must actually end in .onnx (or be renamed with extensions
  visible) for the file dialog and scanner to see it.
* Model loads but audio is garbage / wrong speed -> your export uses a
  different sample rate or hop; set SampleRate / HopLength in the voice
  sidecar .json (HopLength = SampleRate * 0.02 for 20 ms frames).
* "Invalid rank for input: source" -> fixed in the current build: the
  extractor's own declared input shape (rank 1/2/3) is now read from
  the model and used automatically. Re-download / rebuild to get it.
* "Feature extractor / voice mismatch: needs 256-dim ... 768-dim" ->
  your voice model and extractor come from different sets. Pair them:
  vec-256-layer-9.onnx for 256-dim voices, vec-768-layer-12.onnx for
  768-dim voices (both in the ozada/onnx_rvc repo above).
* "tensor ... was not found" -> open the .onnx in Netron, copy the real
  input/output names into the sidecar .json fields.
* AI suddenly stops mid-session -> the error appears on the AI Voices
  status line; toggle AI off/on to reload.
