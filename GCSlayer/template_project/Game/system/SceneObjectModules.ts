/**
 * 该文件为GameCreator编辑器自动生成的代码，请勿修改
 */
/**
 * 场景对象模块基类
 */
class SceneObjectModule {
    static moduleClassArr:(typeof SceneObjectModule)[] = [];
    id: number; // 模块ID
    name: string; // 模块名称
    so: ClientSceneObject; // 场景对象实例
    isDisposed:boolean; // 是否已被销毁
    /**
     * 构造函数
     * @param installCB 用于安装模块的属性值
     */
    constructor(installCB: Callback) {
        installCB && installCB.runWith([this]);
    }
    /**
     * 当移除模块时执行的函数
     */
    onRemoved():void {
        
    }
    /**
     * 刷新：通常在改变了属性需要调用此函数统一刷新效果
     */
    refresh():void {
        
    }
    /**
     * 当卸载模块时执行的函数
     */
    dispose():void {
        this.so = null;
        this.name = null;
        this.isDisposed = true;
    }
}
/**
 * 场景对象公共类，任何场景对象都继承该类
 */
class SceneObjectCommon extends ClientSceneObject {
    constructor(soData: SceneObject, scene: ClientScene) {
        super(soData,scene);
    }
}
/**
 * 场景对象模型：
 */
class SceneObjectModule_1 extends SceneObjectModule {
    constructor(installCB: Callback) {
        super(installCB);
    }
}
SceneObjectModule.moduleClassArr[1]=SceneObjectModule_1;
