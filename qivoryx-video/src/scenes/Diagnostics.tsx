import React from 'react';
import {cue, Copy, Crossfade, Screen, Stage, Tag} from '../design';

export const Diagnostics: React.FC = () => (
  <Stage index={2} name="ДИАГНОСТИКА">
    <Copy eyebrow="01 / НАЧНИТЕ С ФАКТОВ" title={'Сначала —\nпонять\nсистему.'} detail="Реальная нагрузка. Понятные следующие шаги." chips={['CPU', 'Память', 'Накопители']}/>
    <Crossfade at={cue('diagnostics', 'detail', 215)}>
      {[
        <Screen key="overview" file="dark-0" crop={{x: 250, y: 95, w: 1150, h: 735}}/>,
        <Screen key="optimization" file="dark-9" crop={{x: 250, y: 100, w: 1150, h: 720}}/>,
      ]}
    </Crossfade>
    <Tag text="Измерения перед изменениями" x={1020} y={845} delay={65}/>
  </Stage>
);
