/**
 * 该文件为GameCreator编辑器自动生成的代码，请勿修改
 */
/**
 * 材质数据基类
 */
class MaterialData {
    id: number; // 材质编号
    enable: boolean;//是否启用
    ____timeInfo: {[varName:string]: number} = {}; // 储存过渡的当前时间/帧信息，若同一个材质数据需要重置时间复用，可修改该属性后再重新添加材质
}
/**
 * 材质1-色调变更
 */
class MaterialData1 extends MaterialData {
    id: number = 1;
    r: number = 0; // 红 
    g: number = 0; // 绿 
    b: number = 0; // 蓝 
    gray: number = 0; // 灰度 
    mr: number = 1; // 红曝光 
    mg: number = 1; // 绿曝光 
    mb: number = 1; // 蓝曝光 
}
/**
 * 材质2-色相
 */
class MaterialData2 extends MaterialData {
    id: number = 2;
    hue: number = 0; // 色相 
}
/**
 * 材质3-模糊
 */
class MaterialData3 extends MaterialData {
    id: number = 3;
    strength: number = 0; // 强度 
}
/**
 * 材质4-外发光
 */
class MaterialData4 extends MaterialData {
    id: number = 4;
    color: string = "#00FF00"; // 颜色 
    blur: number = 2; // 模糊度 
    offsetX: number = 0; // 水平偏移 
    offsetY: number = 0; // 垂直偏移 
}
