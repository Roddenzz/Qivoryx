import React from 'react';
import {cue, Copy, Crossfade, Screen, Stage, Tag} from '../design';

export const Cleanup: React.FC = () => <Stage index={9} name="ОЧИСТКА И ОБСЛУЖИВАНИЕ">
  <Copy eyebrow="07 / БОЛЬШЕ ПОЛЕЗНОГО МЕСТА" title={'Место\nдля\nважного.'} detail="Анализ перед очисткой. Выбор категорий перед удалением." chips={['Временные файлы', 'Обслуживание дисков']} size={88}/>
  <Crossfade at={cue('cleanup', 'detail', 260)}>
    {[
      <Screen key="cleanup" file="dark-5" crop={{x: 250, y: 110, w: 1150, h: 720}}/>,
      <Screen key="maintenance" file="dark-9" crop={{x: 250, y: 100, w: 1150, h: 720}}/>,
    ]}
  </Crossfade>
  <Tag text="Каждое действие — отдельно" x={1080} y={845} delay={cue('cleanup', 'detail', 260) + 35}/>
</Stage>;
