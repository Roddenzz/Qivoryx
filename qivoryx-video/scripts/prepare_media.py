"""Generate Russian narration, timed captions and local presentation assets."""
from pathlib import Path
import asyncio
import json
import math
import re
import shutil
import sys

PROJECT = Path(__file__).resolve().parents[1]
WORKSPACE = PROJECT.parent
sys.path.insert(0, str(WORKSPACE / '.tools/video-python'))
import edge_tts
from mutagen.mp3 import MP3

SCENES = [
    ('intro', 'Ваш ПК. Под вашим контролем.',
     'Киворикс. Ваш персональный центр управления производительностью Windows. Понимайте, что нагружает компьютер. Настраивайте систему под свои задачи. И проверяйте результат.'),
    ('diagnostics', 'Сначала — понять систему.',
     'Начните с диагностики. Загрузка процессора, память, процессы и диски собраны в одном месте. Центр оптимизации помогает выбрать следующий шаг: от визуальных эффектов до обслуживания накопителей.'),
    ('services', 'Меньше фоновой работы.',
     'Контролируйте автозагрузку и службы. Изучайте назначение службы и нагрузку её процесса перед изменением. Для критических компонентов предусмотрена защита, а отключённые записи автозагрузки можно восстановить.'),
    ('power', 'Производительность. По пунктам.',
     'Настройте питание настольного компьютера. Турбоускорение, политика процессора и охлаждение — с описанием каждого параметра. Изменения применяются к копии плана, чтобы можно было вернуться к исходному.'),
    ('graphics', 'Каждый кадр — вовремя.',
     'В разделе графики проверьте частоту дисплея и рекомендации для Энвидиа и эй эм ди. Новый режим экрана сначала проходит проверку: без подтверждения прежняя частота вернётся автоматически.'),
    ('framelab', 'Проверяйте результат цифрами.',
     'Лаборатория кадров сравнивает реальные игровые замеры из CSV. Средняя частота кадров, редкие просадки и график времени кадра показывают разницу между двумя прогонами. На экране сейчас демонстрационные данные. Собственные результаты можно экспортировать в отчёт.'),
    ('sessions', 'Настройки на время игры.',
     'Игровые сессии включают выбранный план питания на время запуска игры. При необходимости можно поднять приоритет процесса. После завершения сессии приложение возвращает прежние параметры.'),
    ('apps', 'Всё нужное. В одном каталоге.',
     'Сто двадцать шесть приложений: общение, плееры, браузеры и инструменты разработчика. Установка через Вингет, а для поддерживаемых пакетов — резервная загрузка установщика. Рядом — список установленных программ и пакетов Windows.'),
    ('cleanup', 'Освободите место для важного.',
     'Очистка начинается с анализа. Выберите категории временных файлов и удалите ненужное. Для накопителей доступны штатная оптимизация и обслуживание компонентов системы. Каждое действие запускается отдельно.'),
    ('control', 'Ваши настройки. С историей.',
     'Тонкая настройка помогает изменить отклик меню, ускорение мыши, фоновую запись и прозрачность. Резервные копии параметров и журнал операций позволяют видеть изменения и возвращать поддерживаемые настройки.'),
    ('outro', 'Измеряйте. Настраивайте. Проверяйте.',
     'Строгая тёмная тема. Чистая светлая. Единый интерфейс для всех инструментов. Киворикс доступен в портативной версии и с установщиком. Измеряйте. Настраивайте. Проверяйте. Ваш компьютер — под вашим контролем.'),
]
AUDIO_FOLDER = 'voice-arseniy' if '--elevenlabs' in sys.argv else 'voice'


async def generate_voice(index, scene_id, title, narration, semaphore):
    async with semaphore:
        audio_dir = PROJECT / 'public' / AUDIO_FOLDER
        audio_dir.mkdir(parents=True, exist_ok=True)
        audio_path = audio_dir / f'{scene_id}.mp3'
        words_path = audio_dir / f'{scene_id}.words.json'
        if not audio_path.exists() or not words_path.exists():
            if AUDIO_FOLDER == 'voice-arseniy':
                raise RuntimeError(f'Generate ElevenLabs audio for {scene_id} first')
            words = []
            for attempt in range(3):
                try:
                    words = []
                    with audio_path.open('wb') as output:
                        voice = edge_tts.Communicate(narration, voice='ru-RU-DmitryNeural', rate='+3%', boundary='WordBoundary')
                        async for chunk in voice.stream():
                            if chunk['type'] == 'audio':
                                output.write(chunk['data'])
                            elif chunk['type'] == 'WordBoundary':
                                words.append({'text': chunk['text'], 'startMs': chunk['offset'] / 10000, 'endMs': (chunk['offset'] + chunk['duration']) / 10000, 'timestampMs': None, 'confidence': None})
                    if not words:
                        raise RuntimeError('Missing speech timings')
                    words_path.write_text(json.dumps(words, ensure_ascii=False), encoding='utf-8')
                    break
                except Exception:
                    if attempt == 2:
                        raise
                    await asyncio.sleep(2)
        else:
            words = json.loads(words_path.read_text(encoding='utf-8'))
        duration = MP3(audio_path).info.length
        frames = math.ceil(duration * 30) + 38
        # Group actual speech boundaries into short readable subtitle phrases.
        words = [word for word in words if any(c.isalnum() for c in word['text'])]
        search_from = 0
        for w in words:
            match = re.search(re.escape(w['text']), narration[search_from:], flags=re.IGNORECASE)
            if match:
                begin = search_from + match.start()
                end = search_from + match.end()
                while end < len(narration) and not narration[end].isalnum():
                    end += 1
                w['text'] = narration[begin:end].strip()
                search_from = end
        captions = []
        batch = []
        for w in words:
            batch.append(w)
            text = ' '.join(x['text'] for x in batch)
            if len(text) >= 62 or (len(batch) >= 3 and text.endswith(('.', '!', '?', ':', ';'))):
                captions.append({'text': text, 'startMs': batch[0]['startMs'], 'endMs': batch[-1]['endMs'] + 120})
                batch = []
        if batch:
            tail = ' '.join(x['text'] for x in batch)
            if captions and len(tail) < 16 and len(captions[-1]['text']) + len(tail) < 86:
                captions[-1]['text'] += ' ' + tail
                captions[-1]['endMs'] = batch[-1]['endMs'] + 120
            else:
                captions.append({'text': tail, 'startMs': batch[0]['startMs'], 'endMs': batch[-1]['endMs'] + 120})
        for current, following in zip(captions, captions[1:]):
            current['endMs'] = min(current['endMs'], following['startMs'])
        for caption in captions:
            caption['endMs'] = min(caption['endMs'], duration * 1000)
            # Spoken pronunciation belongs in narration; branded spelling belongs on screen.
            for spoken, written in [('Киворикс', 'Qivoryx'), ('Энвидиа', 'NVIDIA'), ('эй эм ди', 'AMD'), ('Вингет', 'winget'), ('Сто двадцать шесть', '126')]:
                caption['text'] = caption['text'].replace(spoken, written)
        cue_words = {
            'diagnostics': {'detail': 'Центр'},
            'services': {'detail': 'Изучайте'},
            'power': {'detail': 'Турбоускорение', 'tag': 'Изменения'},
            'graphics': {'tag': 'Новый'},
            'framelab': {'chart': 'график'},
            'sessions': {'restore': 'возвращает'},
            'apps': {'detail': 'Рядом'},
            'cleanup': {'detail': 'Для'},
            'control': {'tag': 'Резервные'},
            'outro': {'ending': 'Киворикс'},
        }
        cues = {}
        for name, token in cue_words.get(scene_id, {}).items():
            word = next(w for w in words if re.sub(r'[^\w]', '', w['text']).casefold() == token.casefold())
            cues[name] = 14 + round(word['startMs'] / 1000 * 30)
        print(f'{scene_id}: {duration:.2f}s / {frames} frames / {len(words)} words', flush=True)
        return {'id': scene_id, 'title': title, 'narration': narration, 'audio': f'{AUDIO_FOLDER}/{scene_id}.mp3', 'duration': duration, 'frames': frames, 'voiceOffset': 14, 'captions': captions, 'cues': cues}


def srt_stamp(ms):
    value = max(0, round(ms))
    h, value = divmod(value, 3600000)
    m, value = divmod(value, 60000)
    s, value = divmod(value, 1000)
    return f'{h:02}:{m:02}:{s:02},{value:03}'


async def main():
    assets = PROJECT / 'public/screens'
    assets.mkdir(parents=True, exist_ok=True)
    for name in ['dark-0', 'light-0', 'dark-9', 'dark-10', 'dark-service-detail', 'dark-2', 'dark-power-scrolled', 'dark-14', 'dark-frame-comparison', 'dark-frame-chart', 'dark-13', 'dark-3', 'dark-4', 'dark-5', 'dark-11', 'dark-8']:
        source = WORKSPACE / 'artifacts/ui' / f'{name}.png'
        target = assets / f'{name}.png'
        if source.exists():
            shutil.copy2(source, target)
        elif not target.exists():
            raise FileNotFoundError(f'Missing screenshot: {source}; run the app with --smoke first')
    shutil.copy2(WORKSPACE / 'Tweaker/Assets/brand.png', PROJECT / 'public/brand.png')
    semaphore = asyncio.Semaphore(2)
    scenes = await asyncio.gather(*(generate_voice(i, *scene, semaphore) for i, scene in enumerate(SCENES)))
    cursor = 0
    subtitles = []
    for scene in scenes:
        scene['from'] = cursor
        for cap in scene['captions']:
            offset = (cursor + scene['voiceOffset']) / 30 * 1000
            subtitles.append(f"{len(subtitles)+1}\n{srt_stamp(offset + cap['startMs'])} --> {srt_stamp(offset + cap['endMs'])}\n{cap['text']}\n")
        cursor += scene['frames'] - 18
    data = {'fps': 30, 'transition': 18, 'totalFrames': cursor + 18, 'scenes': scenes}
    (PROJECT / 'src/timeline.json').write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
    (PROJECT / 'public/Qivoryx-RU.srt').write_text('\n'.join(subtitles), encoding='utf-8-sig')
    (PROJECT / 'public/narration.txt').write_text('\n\n'.join(f"{s[1]}\n{s[2]}" for s in SCENES), encoding='utf-8')
    print(f"TOTAL: {data['totalFrames']/30:.2f}s / {data['totalFrames']} frames", flush=True)


if __name__ == '__main__':
    asyncio.run(main())
