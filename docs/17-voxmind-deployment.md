# Déploiement VoxMind (STT Parakeet + TTS F5-TTS)

Ce chapitre décrit comment alimenter le module `Seren.Modules.VoxMind` avec les bundles ONNX nécessaires pour activer la transcription locale (Parakeet TDT 0.6B v3) et la synthèse vocale multilingue (F5-TTS) côté serveur.

> Sans ces bundles, le module reste fonctionnel : il no-op silencieusement sur les requêtes voix et l'éventuel `AudioModule` (NoOp / OpenAI) reprend la main. Les health checks `voxmind:parakeet` et `voxmind:f5tts` retournent `Degraded` plutôt que `Unhealthy`.

## 1. Architecture des modèles

Le module attend deux familles de modèles dans un volume partagé `voxmind_models`, monté en lecture seule sur le conteneur `seren-api` à `/data/voxmind/models`.

### Layout disque attendu

```
/data/voxmind/models/
├── parakeet/                            # STT engine #1 (Parakeet TDT v3 INT8)
│   ├── nemo128.onnx                     # mel spectrogram preprocessor
│   ├── encoder-model.int8.onnx          # Parakeet encoder (int8 quantized)
│   ├── decoder_joint-model.int8.onnx    # TDT decoder/joint
│   └── vocab.txt                        # vocabulary
├── whisper-small/                       # STT engine #2 (Whisper sherpa-onnx)
│   ├── small-encoder.int8.onnx          # Whisper encoder (int8)
│   ├── small-decoder.int8.onnx          # Whisper decoder (int8)
│   └── small-tokens.txt                 # sentencepiece tokens
└── f5-tts/                              # TTS engines
    ├── fr/
    │   ├── F5_Preprocess.onnx           # text + audio ref → embeddings
    │   ├── F5_Transformer.onnx          # DiT transformer (flow-matching)
    │   ├── F5_Decode.onnx               # Vocos vocoder 24 kHz
    │   ├── tokens.txt                   # phoneme/char vocabulary
    │   ├── reference.wav                # default voice prompt (PCM 24 kHz mono, < 30 s)
    │   └── reference.txt                # transcription exacte de reference.wav
    └── en/
        └── ... (même structure que fr/)
```

> Les deux moteurs STT cohabitent : l'utilisateur sélectionne celui qui sert
> via l'onglet Settings *Reconnaissance vocale* (package `@seren/module-voxmind`).
> Parakeet est la valeur par défaut (latence ~5 s sur CPU) ; Whisper est plus
> lent (~10-15 s pour les bouts de phrase courts) mais bien meilleur en
> français. Tu peux ne déployer qu'un des deux : le moteur manquant retourne
> simplement `Degraded` sur son health check et le router fallback sur l'autre.

## 2. Téléchargement des bundles

### Parakeet TDT 0.6B v3 (STT, 25 langues)

Le bundle pré-export INT8 est sur HuggingFace : [`smcleod/parakeet-tdt-0.6b-v3-int8`](https://huggingface.co/smcleod/parakeet-tdt-0.6b-v3-int8).

```bash
# Sur la machine hôte, dans un répertoire de staging :
mkdir -p ./voxmind-bundle/parakeet
cd ./voxmind-bundle/parakeet
huggingface-cli download smcleod/parakeet-tdt-0.6b-v3-int8 \
  nemo128.onnx encoder-model.int8.onnx decoder_joint-model.int8.onnx vocab.txt \
  --local-dir .
```

Taille : ~620 MB (4 fichiers).

### Whisper variants (STT, sherpa-onnx)

> **Setup ownership** : le volume `voxmind_models` est créé root-owned au
> premier `docker compose up`. Le container `seren-api` tourne en
> `appuser:999`, donc avant le premier download depuis l'UI il faut
> `chown` une seule fois :
>
> ```bash
> docker compose exec -u root seren-api chown -R 999:999 /data/voxmind/models
> ```
>
> Pas besoin de re-jouer si le volume est conservé entre les runs.

Depuis l'introduction du gestionnaire de modèles (Settings → "Reconnaissance
vocale"), **les variants Whisper sont téléchargeables directement depuis
l'UI** : chaque ligne de la liste expose une icône ↓ qui pousse les fichiers
ONNX dans `voxmind_models:/data/voxmind/models/whisper-{tiny|base|small|medium|large}/`.
La désinstallation se fait via l'icône ✕ sur la ligne d'un variant déjà
téléchargé (sauf le dernier moteur disponible, protégé côté UI + API).

Le pré-déploiement manuel reste supporté quand le serveur n'a pas accès
internet — pose les fichiers dans le sous-dossier attendu, ils apparaîtront
comme `isDownloaded: true` au prochain `GET /api/voxmind/models` :

```bash
# Exemple : Whisper Small pour FR (recommandé en mode déploiement manuel)
mkdir -p /var/lib/docker/volumes/seren_voxmind_models/_data/whisper-small
cd /var/lib/docker/volumes/seren_voxmind_models/_data/whisper-small
huggingface-cli download csukuangfj/sherpa-onnx-whisper-small \
  small-encoder.int8.onnx small-decoder.int8.onnx small-tokens.txt \
  --local-dir .
```

Tailles approximatives : `tiny` ~75 MB, `base` ~140 MB, `small` ~470 MB
(recommandé pour FR), `medium` ~1.5 GB, `large` ~3 GB.

Le router serveur reçoit l'engineHint sous la forme `whisper-{size}`
(envoyé inline par l'UI dans `VoiceInputPayload.sttEngine`) et résout le
bon bundle. La valeur historique `Modules:voxmind:Stt:Whisper:ModelSize`
n'est plus consultée que pour le legacy hint `engine=whisper` sans
suffixe — laisse-la sur `"small"` ou la valeur de ton choix.

### F5-TTS (TTS, port DakeQQ ONNX)

Le pipeline d'export ONNX vit sur [`DakeQQ/F5-TTS-ONNX`](https://github.com/DakeQQ/F5-TTS-ONNX). Pour chaque langue, il faut un fine-tune compatible :

- **FR** : [`RASPIAUDIO/F5-French-MixedSpeakers-reduced`](https://huggingface.co/RASPIAUDIO/F5-French-MixedSpeakers-reduced) (~600 MB après export INT8).
- **EN** : [`SWivid/F5-TTS`](https://huggingface.co/SWivid/F5-TTS) base anglais (~600 MB après export).

Suivre la procédure DakeQQ pour produire les 3 fichiers ONNX + `tokens.txt`. La voix de référence (`reference.wav` + `reference.txt`) est libre — un extrait court (~10-15 s) de votre enregistrement le plus représentatif suffit.

## 3. Alimentation du volume Docker

Une fois le staging préparé, copier le tout dans le volume nommé :

```bash
# Le conteneur seren-api doit être stoppé pour que le volume soit montable ailleurs
docker compose stop seren-api

# Créer le volume si nécessaire (Compose le crée automatiquement au premier up)
docker volume create voxmind_models

# Copier le staging dans le volume via un conteneur transitoire
docker run --rm \
  -v voxmind_models:/dst \
  -v "$(pwd)/voxmind-bundle":/src:ro \
  busybox sh -c 'cp -r /src/. /dst/ && chown -R 1000:1000 /dst'

# Redémarrer seren-api : il va loader les bundles au premier appel
docker compose up -d seren-api
```

## 4. Vérification post-déploiement

```bash
# 1. Health checks doivent passer Healthy
curl -fsS http://localhost:5080/health/ready | jq '.entries'

# 2. Logs : rechercher la signature de chargement
docker compose logs seren-api | grep -E 'VoxMind STT|VoxMind TTS'
# Attendu :
#   VoxMind STT: Parakeet ONNX bundle loaded from /data/voxmind/models/parakeet (1024 tokens).
#   VoxMind TTS: 2 language(s) available on demand: fr, en.

# 3. Smoke test transcription FR via WebSocket (utilise le SDK ou un wav de test)
#    Le résultat doit contenir "language":"fr" et un audio WAV 24 kHz dans la réponse TTS.
```

## 5. Dépannage

| Symptôme | Cause probable | Fix |
|---|---|---|
| `voxmind:parakeet` reste `Degraded` après mise en place | `Modules:voxmind:Stt:ModelDir` env var non propagée à seren-api | Vérifier `docker compose config` ; redémarrer le service |
| `voxmind:parakeet` retourne `Unhealthy: missing nemo128.onnx` | Volume monté mais bundle incomplet | Compléter le bundle dans le volume puis `docker compose restart seren-api` |
| Logs : `VoxMind STT: failed to decode webm audio` | `ffmpeg` absent du PATH dans l'image | Confirmer que le `Dockerfile` fait bien `apt-get install ffmpeg` |
| Latence 1ère synthèse FR > 5 s, 2ème synthèse FR < 1 s | Cold-load attendu ; le pre-warm du `SubmitVoiceInputHandler` ne se déclenche que pour la voie chat (pas l'API directe) | Comportement nominal pour les appels HTTP directs ; OK |
| Synthèse FR retourne un WAV silencieux | `reference.wav` est trop court ou de mauvaise qualité | Remplacer par un extrait propre 10-15 s, mêmes locuteur/timbre cible ; `reference.txt` doit transcrire **exactement** la WAV |

## 6. Mise à jour des modèles

Aucun mécanisme de hot-reload : les `InferenceSession` sont chargées au premier usage et restent résidentes dans le LRU cache. Pour swap un checkpoint :

```bash
# 1. Mettre à jour le volume
docker run --rm -v voxmind_models:/dst -v "$(pwd)/voxmind-bundle":/src:ro \
  busybox sh -c 'cp -r /src/. /dst/'

# 2. Restart seren-api pour relâcher les sessions ONNX
docker compose restart seren-api
```

Le redémarrage est gracieux : `Dispose()` attend (5 s timeout) la fin de l'inférence en cours avant de relâcher les handles natifs.

## 7. Référence rapide options

Section `Modules:voxmind` (`appsettings.json` ou env `Modules__voxmind__*`) :

| Clé | Type | Défaut | Description |
|---|---|---|---|
| `Enabled` | bool | `true` | Désactiver = AudioModule (NoOp/OpenAI) reprend la main |
| `DefaultLanguage` | ISO 639-1 | `"fr"` | Fallback quand la détection retourne `"und"` |
| `Stt.DefaultEngine` | enum | `"parakeet"` | Moteur utilisé quand l'UI ne fournit pas de hint (`"parakeet"` ou `"whisper"`) |
| `Stt.MaxChunkSeconds` | double | `12.0` | Durée max avant découpage de l'audio en input |
| `Stt.Parakeet.ModelDir` | path | `""` | Bundle Parakeet (vide = engine désactivé) |
| `Stt.Whisper.ModelDir` | path | `""` | Bundle Whisper sherpa-onnx (vide = engine désactivé) |
| `Stt.Whisper.ModelSize` | enum | `"small"` | Variant Whisper (`tiny`/`base`/`small`/`medium`/`large`) — préfixe des fichiers |
| `Stt.Whisper.Language` | ISO 639-1 ? | `null` | Hint de langue forcé (null = laisse Whisper auto-détecter, mais l'API sherpa-onnx ne propage pas la langue détectée) |
| `Stt.ModelDir` | path | `""` | **Legacy** — mappé automatiquement vers `Stt.Parakeet.ModelDir` au boot avec un warning. À supprimer après migration |
| `Tts.FlowMatchingSteps` | int | `32` | Étapes Euler du transformer F5 (qualité ↑ / latence ↑) |
| `Tts.CacheCapacity` | int | `2` | Nombre d'engines F5 résidents simultanément |
| `Tts.Languages.<iso>.*` | object | `{}` | Checkpoints F5 par langue (cf. layout disque) |

Voir aussi : [`docs/02-architecture-globale.md`](02-architecture-globale.md) pour le pipeline voix end-to-end et [`docs/09-plugins.md`](09-plugins.md) pour le contrat `ISerenModule`.
