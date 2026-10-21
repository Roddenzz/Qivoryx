import React from 'react';
import {AbsoluteFill, Interactive, interpolate, useCurrentFrame} from 'remotion';
import {clamp, cue, ease, enter, QMark, Screen, Stage} from '../design';

export const Outro: React.FC = () => {
  const frame = useCurrentFrame();
  const endingAt = cue('outro', 'ending', 192);
  const ending = interpolate(frame, [endingAt, endingAt + 35], [0, 1], {...clamp, easing: ease});
  return <AbsoluteFill>
    <Stage index={11} name="ЕДИНЫЙ ИНТЕРФЕЙС" light>
      <div style={{opacity: 1 - ending}}>
        <Interactive.Div name="Один интерфейс, две темы" style={{position: 'absolute', left: 94, top: 155, fontSize: 90, letterSpacing: -3.5, fontWeight: 600, whiteSpace: 'nowrap', opacity: interpolate(frame, [0, 28], [0, 1], clamp)}}>Два характера. Один Qivoryx.</Interactive.Div>
        <Screen file="dark-0" x={94} y={338} w={843} h={551} delay={15} zoom={1.005}/>
        <Screen file="light-0" x={983} y={338} w={843} h={551} delay={35} zoom={1.005} light/>
        <Interactive.Div name="Тёмная тема" style={{position: 'absolute', left: 100, top: 283, fontSize: 28, color: '#444'}}>ТЁМНАЯ</Interactive.Div>
        <Interactive.Div name="Светлая тема" style={{position: 'absolute', left: 988, top: 283, fontSize: 28, color: '#444'}}>СВЕТЛАЯ</Interactive.Div>
      </div>
    </Stage>
    <AbsoluteFill style={{opacity: ending}}>
      <Stage index={11} name="ВАШ КОМПЬЮТЕР. ВАШ КОНТРОЛЬ.">
        <Interactive.Div name="Измеряйте, настраивайте, проверяйте" style={{position: 'absolute', left: 100, top: 224, fontSize: 110, fontWeight: 550, letterSpacing: -4.5, lineHeight: 1.13, translate: interpolate(frame, [endingAt + 8, endingAt + 53], ['0px 50px', '0px 0px'], {...clamp, easing: ease})}}>Измеряйте.<br/>Настраивайте.<br/>Проверяйте.</Interactive.Div>
        <div style={{position: 'absolute', right: 215, top: 210, opacity: enter(frame, endingAt + 23, 45), scale: interpolate(frame, [endingAt + 23, endingAt + 318], [0.96, 1.025], clamp)}}><QMark size={360}/></div>
        <Interactive.Div name="Финальное название" style={{position: 'absolute', right: 185, top: 650, fontSize: 63, fontWeight: 620, letterSpacing: 2}}>QIVORYX</Interactive.Div>
        <div style={{position: 'absolute', left: 105, top: 752, display: 'flex', gap: 20, opacity: enter(frame, endingAt + 33)}}>
          {['Портативная версия', 'Установщик'].map((text) => <div key={text} style={{padding: '18px 28px', border: '1px solid #777', borderRadius: 17, fontSize: 32}}>{text}</div>)}
        </div>
        <Interactive.Div name="Поддерживаемые платформы" style={{position: 'absolute', left: 107, top: 858, color: '#a7a7ad', fontSize: 27, opacity: interpolate(frame, [endingAt + 48, endingAt + 80], [0, 1], clamp)}}>Windows 10 / 11 · x64</Interactive.Div>
        <div style={{position: 'absolute', right: 185, top: 865, fontSize: 21, color: '#888', opacity: enter(frame, endingAt + 153)}}>Озвучка: ElevenLabs</div>
      </Stage>
    </AbsoluteFill>
  </AbsoluteFill>;
};
