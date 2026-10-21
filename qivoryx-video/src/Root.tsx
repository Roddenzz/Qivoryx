import {Composition, Folder} from 'remotion';
import {Presentation, sceneComponents} from './Composition';
import timeline from './timeline.json';

export const RemotionRoot: React.FC = () => {
  return (
    <>
      <Composition id="Qivoryx" component={Presentation} durationInFrames={timeline.totalFrames} fps={30} width={1920} height={1080}/>
      <Folder name="Scenes">
        {timeline.scenes.map((scene, i) => <Composition key={scene.id} id={scene.id} component={sceneComponents[i]} durationInFrames={scene.frames} fps={30} width={1920} height={1080}/>)}
      </Folder>
    </>
  );
};
