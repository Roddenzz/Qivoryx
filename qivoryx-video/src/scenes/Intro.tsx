import React from 'react';
import {Interactive, interpolate, useCurrentFrame} from 'remotion';
import {clamp, ease, QMark, Screen, Stage} from '../design';

export const Intro: React.FC = () => {
  const frame = useCurrentFrame();
  return (
    <Stage index={1} name="ВАШ ПК. ВАШИ ПРАВИЛА.">
      <div style={{position: 'absolute', left: 1285, top: 135, opacity: 0.06, rotate: `${interpolate(frame, [0, 475], [-18, -5], clamp)}deg`}}>
        <QMark size={690}/>
      </div>
      <Interactive.Div name="Название Qivoryx" style={{position: 'absolute', left: 85, top: 190, fontSize: 190, fontWeight: 650, letterSpacing: -11, opacity: interpolate(frame, [0, 38], [0, 1], clamp), translate: interpolate(frame, [0, 48], ['0px 65px', '0px 0px'], {...clamp, easing: ease})}}>
        QIVORYX
      </Interactive.Div>
      <Interactive.Div name="Обещание продукта" style={{position: 'absolute', left: 98, top: 465, fontSize: 88, fontWeight: 550, lineHeight: 1.13, letterSpacing: -3, opacity: interpolate(frame, [32, 65], [0, 1], clamp), translate: interpolate(frame, [32, 80], ['0px 40px', '0px 0px'], {...clamp, easing: ease})}}>
        Ваш ПК.<br/>Под вашим<br/>контролем.
      </Interactive.Div>
      <Screen file="dark-0" x={934} y={390} w={894} h={545} delay={60} crop={{x: 0, y: 35, w: 1440, h: 880}} zoom={1.045}/>
      <Interactive.Div name="Три шага" style={{position: 'absolute', left: 101, top: 868, color: '#a0a0a6', fontSize: 28, letterSpacing: 1, opacity: interpolate(frame, [80, 110], [0, 1], clamp)}}>
        ДИАГНОСТИКА / НАСТРОЙКА / ПРОВЕРКА
      </Interactive.Div>
    </Stage>
  );
};
