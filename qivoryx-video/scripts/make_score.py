"""An original, deliberately quiet electronic underscore; no sampled music."""
from pathlib import Path
import json
import subprocess
import sys
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT.parent / '.tools/video-python'))
import imageio_ffmpeg

timeline = json.loads((ROOT / 'src/timeline.json').read_text(encoding='utf-8'))
duration = timeline['totalFrames'] / 30
sr = 44100
path = ROOT / 'public/score.wav'
progression = [[50,57,60,65], [46,53,58,62], [53,57,60,65], [48,55,60,64]]

def frequency(midi):
    return 440 * 2 ** ((midi-69)/12)

with wave.open(str(path), 'wb') as output:
    output.setnchannels(2)
    output.setsampwidth(2)
    output.setframerate(sr)
    for second in range(int(np.ceil(duration))):
        count = min(sr, int(round(duration*sr))-second*sr)
        t = (np.arange(count, dtype=np.float64)+second*sr)/sr
        left = np.zeros(count)
        right = np.zeros(count)
        for bar_start in range(max(0, second//16-1)*16, (second//16+1)*16+1, 16):
            age = t-bar_start
            env = np.clip(age/2.0, 0, 1)*np.clip((18-age)/2.0, 0, 1)
            notes = progression[(bar_start//16)%4]
            for n, note in enumerate(notes):
                f = frequency(note)
                swell = 0.82+0.18*np.sin(t*0.47+n)
                left += 0.0032*env*swell*np.sin(2*np.pi*f*t + n*0.31)
                right += 0.0032*env*swell*np.sin(2*np.pi*f*1.0007*t + n*0.31)
            left += 0.006*env*np.sin(2*np.pi*frequency(notes[0]-12)*t)
            right += 0.006*env*np.sin(2*np.pi*frequency(notes[0]-12)*t)
        # A soft two-note pulse keeps long UI close-ups moving without competing with speech.
        pulse_age = np.mod(t, 0.6)
        pulse = (1-np.exp(-pulse_age*60))*np.exp(-pulse_age*9)
        note = np.where((np.floor(t/0.6).astype(int)%4)<2, frequency(74), frequency(69))
        left += 0.0035*pulse*np.sin(2*np.pi*note*t)
        right += 0.0035*pulse*np.sin(2*np.pi*note*t+0.15)
        master = np.clip(t/3, 0, 1)*np.clip((duration-t)/3, 0, 1)
        stereo = np.stack((left*master, right*master), axis=1)
        output.writeframes((np.clip(stereo,-1,1)*32767).astype('<i2').tobytes())
subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), '-hide_banner', '-loglevel', 'error', '-y', '-i', str(path), '-c:a', 'aac', '-b:a', '192k', str(ROOT/'public/score.m4a')], check=True)
path.unlink()
print(f'Original stereo underscore: {duration:.2f}s')
