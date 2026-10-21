import React from 'react';
import {cue, Copy, Screen, Stage, Tag} from '../design';

export const Graphics: React.FC = () => (
  <Stage index={5} name="ГРАФИКА И ДИСПЛЕЙ">
    <Copy eyebrow="04 / ПЛАВНОСТЬ И ЗАДЕРЖКА" title={'Каждый\nкадр —\nвовремя.'} detail="Рекомендации для вашей видеокарты. Контроль частоты экрана." chips={['NVIDIA', 'AMD', 'Герцы']} size={84}/>
    <Screen file="dark-14" y={260} h={530} crop={{x: 261, y: 250, w: 1135, h: 545}} zoom={1.005}/>
    <Tag text="15 секунд на подтверждение режима" x={970} y={845} delay={cue('graphics', 'tag', 175)}/>
  </Stage>
);
