import React from 'react';
import {cue, Copy, Crossfade, Screen, Stage, Tag} from '../design';

export const Services: React.FC = () => (
  <Stage index={3} name="ФОНОВАЯ РАБОТА">
    <Copy eyebrow="02 / АВТОЗАПУСК И СЛУЖБЫ" title={'Меньше\nлишнего\nв фоне.'} detail="Сначала назначение и нагрузка. Затем — решение." chips={['Защита важных служб', 'Восстановление']}/>
    <Crossfade at={cue('services', 'detail', 112)}>
      {[
        <Screen key="startup" file="dark-10" crop={{x: 250, y: 115, w: 1150, h: 710}}/>,
        <React.Fragment key="service">
          <Screen file="dark-service-detail" y={263} h={148} crop={{x: 261, y: 225, w: 1135, h: 152}} zoom={1.005}/>
          <Screen file="dark-service-detail" y={465} h={178} crop={{x: 261, y: 700, w: 1135, h: 183}} zoom={1.005}/>
          <div style={{position: 'absolute', left: 749, top: 705, fontSize: 34, color: '#c7c7cd'}}>Назначение · состояние · влияние</div>
        </React.Fragment>,
      ]}
    </Crossfade>
    <Tag text="Назначение службы — в деталях" x={1000} y={844} delay={cue('services', 'detail', 112) + 30}/>
  </Stage>
);
