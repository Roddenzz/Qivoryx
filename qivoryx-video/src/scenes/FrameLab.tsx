import React from 'react';
import {Interactive, interpolate, useCurrentFrame} from 'remotion';
import {cue, clamp, Crossfade, ease, Screen, Stage} from '../design';

export const FrameLab: React.FC = () => {
  const frame = useCurrentFrame();
  return <Stage index={6} name="ЛАБОРАТОРИЯ КАДРОВ">
    <Interactive.Div
      name="Проверяемый результат"
      style={{position: 'absolute', left: 93, top: 162, fontSize: 85, fontWeight: 600, letterSpacing: -3.5, whiteSpace: 'nowrap', opacity: interpolate(frame, [0, 28], [0, 1], clamp), translate: interpolate(frame, [0, 40], ['0px 40px', '0px 0px'], {...clamp, easing: ease})}}
    >Результат, который можно проверить.</Interactive.Div>
    <Interactive.Div name="Порядок сравнения" style={{position: 'absolute', left: 96, top: 283, fontSize: 33, color: '#bcbcc2', opacity: interpolate(frame, [16, 48], [0, 1], clamp)}}>Импорт CSV → сравнение A / B → отчёт</Interactive.Div>
    <Interactive.Div name="Маркировка демонстрационных данных" style={{position: 'absolute', right: 95, top: 287, fontSize: 23, border: '1px solid #888', borderRadius: 9, padding: '9px 17px', color: '#fff', letterSpacing: 1}}>ДЕМОНСТРАЦИОННЫЕ ДАННЫЕ</Interactive.Div>
    <Crossfade at={cue('framelab', 'chart', 278)}>
      {[
        <Screen key="comparison" file="dark-frame-comparison" x={96} y={378} w={1728} h={490} crop={{x: 270, y: 390, w: 1110, h: 315}} delay={15} zoom={1.005}/>,
        <Screen key="chart" file="dark-frame-chart" x={96} y={378} w={1728} h={490} crop={{x: 270, y: 347, w: 1110, h: 315}} delay={15} zoom={1.005}/>,
      ]}
    </Crossfade>
    <div style={{position: 'absolute', left: 99, top: 895, display: 'flex', gap: 45, fontSize: 27, color: '#babac0', opacity: interpolate(frame, [55, 80], [0, 1], clamp)}}>
      <span>Средний FPS</span><span>1% low</span><span>P95 / P99</span><span>Время кадра</span>
    </div>
  </Stage>;
};
