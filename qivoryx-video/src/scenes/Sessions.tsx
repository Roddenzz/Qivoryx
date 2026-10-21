import React from 'react';
import {interpolate, useCurrentFrame} from 'remotion';
import {cue, clamp, Copy, enter, Screen, Stage} from '../design';

export const Sessions: React.FC = () => {
  const frame = useCurrentFrame();
  return <Stage index={7} name="ИГРОВЫЕ СЕССИИ">
    <Copy eyebrow="05 / ВРЕМЕННЫЙ ПРОФИЛЬ" title={'На время\nигры.\nС возвратом.'} detail="План питания и приоритет процесса в рамках одной сессии." chips={['EXE или процесс', 'Возврат параметров']} size={78}/>
    <Screen file="dark-13" y={214} h={430} crop={{x: 260, y: 115, w: 1130, h: 440}}/>
    <div style={{position: 'absolute', left: 724, top: 681, width: 1104, height: 207, background: 'linear-gradient(145deg, #28282b, #131315)', border: '1px solid #525256', borderRadius: 24, opacity: enter(frame, 60)}}>
      <div style={{position: 'absolute', left: 80, right: 80, top: 68, height: 2, background: '#555'}}>
        <div style={{height: 2, width: `${interpolate(frame, [65, cue('sessions', 'restore', 340) + 14], [0, 100], clamp)}%`, background: '#eee'}}/>
      </div>
      {['Запуск', 'Ваш профиль', 'Возврат'].map((text, i) => <div key={text} style={{position: 'absolute', top: 47, left: 70 + i * 375, textAlign: 'center', width: 210, translate: '-50px 0px', opacity: enter(frame, 65 + i * 55)}}>
        <div style={{width: 42, height: 42, borderRadius: 22, background: '#e7e7e7', border: '8px solid #252527', margin: '0 auto 22px'}}/>
        <div style={{fontSize: 28, color: i === 2 ? '#fff' : '#c5c5ca'}}>{text}</div>
      </div>)}
    </div>
  </Stage>;
};
