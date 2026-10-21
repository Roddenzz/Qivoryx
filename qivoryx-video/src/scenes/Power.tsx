import React from 'react';
import {cue, Copy, Crossfade, Screen, Stage, Tag} from '../design';

export const Power: React.FC = () => (
  <Stage index={4} name="ПЛАН ПИТАНИЯ">
    <Copy eyebrow="03 / ДЛЯ НАСТОЛЬНОГО ПК" title={'Мощность.\nПод вашим\nконтролем.'} detail="Подробные параметры питания с объяснением влияния." chips={['Турбо', 'Процессор', 'Охлаждение']} size={74}/>
    <Crossfade at={cue('power', 'detail', 135)}>
      {[
        <Screen key="plan" file="dark-2" crop={{x: 250, y: 100, w: 1150, h: 720}}/>,
        <React.Fragment key="parameters">
          <Screen file="dark-power-scrolled" y={225} h={173} crop={{x: 261, y: 225, w: 1135, h: 178}} zoom={1.005}/>
          <Screen file="dark-power-scrolled" y={428} h={246} crop={{x: 261, y: 418, w: 1135, h: 253}} zoom={1.005}/>
          <Screen file="dark-power-scrolled" y={704} h={133} crop={{x: 261, y: 687, w: 1135, h: 137}} zoom={1.005}/>
        </React.Fragment>,
      ]}
    </Crossfade>
    <Tag text="Работа с копией плана питания" x={1000} y={845} delay={cue('power', 'tag', 250)}/>
  </Stage>
);
