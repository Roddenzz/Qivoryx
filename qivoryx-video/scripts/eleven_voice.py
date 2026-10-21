"""Optional ElevenLabs narration. Credentials are read only from the environment."""
import base64
import json
import os
from pathlib import Path
import re
import sys
import time
import urllib.error
import urllib.request
from prepare_media import SCENES

ROOT = Path(__file__).resolve().parents[1]
VOICE = 'fmZEtrbS5wudON3ynVP2'  # Arseniy — the Russian voice selected by the user.
KEY = os.environ.get('ELEVENLABS_API_KEY')
if not KEY:
    raise SystemExit('Set ELEVENLABS_API_KEY in the process environment.')


def api(path, payload):
    request = urllib.request.Request('https://api.elevenlabs.io/v1/'+path, data=json.dumps(payload, ensure_ascii=False).encode('utf-8'), headers={'xi-api-key': KEY, 'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(request, timeout=150) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        detail = json.loads(error.read().decode()).get('detail', {})
        # Status and human-readable API error only; never log headers or credentials.
        print(json.dumps({'http': error.code, 'detail': detail}, ensure_ascii=True), flush=True)
        raise SystemExit('Voice generation stopped; completed audio has been preserved.') from None


def main():
    out = ROOT / 'public/voice-arseniy'
    out.mkdir(parents=True, exist_ok=True)
    start = time.monotonic()
    for index, (scene_id, _, text) in enumerate(SCENES):
        audio = out / f'{scene_id}.mp3'
        words_path = out / f'{scene_id}.words.json'
        if audio.exists() and words_path.exists():
            print(f'{scene_id}: cached', flush=True)
            continue
        result = api(f'text-to-speech/{VOICE}/with-timestamps?output_format=mp3_44100_128', {
            'text': text,
            'model_id': 'eleven_multilingual_v2',
            'seed': 42,
            'voice_settings': {'stability': 0.56, 'similarity_boost': 0.75, 'style': 0.15, 'use_speaker_boost': True, 'speed': 1.03},
            'previous_text': SCENES[index-1][2] if index else None,
            'next_text': SCENES[index+1][2] if index+1 < len(SCENES) else None,
        })
        alignment = result.get('normalized_alignment') or result['alignment']
        characters = ''.join(alignment['characters'])
        words = []
        for match in re.finditer(r'\S+', characters):
            words.append({'text': match.group(), 'startMs': alignment['character_start_times_seconds'][match.start()]*1000, 'endMs': alignment['character_end_times_seconds'][match.end()-1]*1000, 'timestampMs': None, 'confidence': None})
        audio.write_bytes(base64.b64decode(result['audio_base64']))
        words_path.write_text(json.dumps(words, ensure_ascii=False), encoding='utf-8')
        print(f'{index+1}/{len(SCENES)} {scene_id}: {len(words)} aligned words; elapsed {time.monotonic()-start:.1f}s', flush=True)
        if '--sample' in sys.argv:
            break
    (out / 'provider.json').write_text(json.dumps({'provider': 'ElevenLabs', 'model': 'eleven_multilingual_v2', 'voice': 'Arseniy', 'voice_id': VOICE, 'language': 'ru'}, ensure_ascii=False, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
