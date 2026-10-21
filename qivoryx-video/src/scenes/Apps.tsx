import React from 'react';
import {cue, Copy, Crossfade, Screen, Stage, Tag} from '../design';

export const Apps: React.FC = () => <Stage index={8} name="ПРИЛОЖЕНИЯ">
  <Copy eyebrow="06 / УСТАНОВКА И УДАЛЕНИЕ" title={'126\nприложений.\nОдин каталог.'} detail="Общение, медиа и разработка. Рядом — управление установленным." chips={['winget', 'Резервная загрузка']} size={74}/>
  <Crossfade at={cue('apps', 'detail', 355)}>
    {[
      <Screen key="catalog" file="dark-3" crop={{x: 250, y: 95, w: 1150, h: 720}}/>,
      <Screen key="uninstall" file="dark-4" crop={{x: 250, y: 110, w: 1150, h: 720}}/>,
    ]}
  </Crossfade>
  <Tag text="Программы + пакеты Windows" x={1040} y={845} delay={cue('apps', 'detail', 355) + 30}/>
</Stage>;
