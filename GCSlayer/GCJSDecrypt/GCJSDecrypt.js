console.log("GCJSDecrypt by print_0")

const fs = require('fs');
const path = require('path');

const codePath = process.argv[2];
console.log(codePath)

let originCode = '';
const orderList = [];

const flag = `//<<JS加密 by GameCreator  https://www.gamecreator.com.cn>>\n`;
// const splitF='./split/';
const toRegex = function (text) {
    return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
const encryptFunction = function (code) {
    if (code.startsWith(flag)) {
        code = code.replaceAll(/s = [thisfunctioneval\[\]_+0-9a-f();{},]+(this\[[_+0-9a-z]+\])='';/g, 'encryptFunction($1);$1=\'\';');
        orderList.length++;
        orderList[orderList.length - 1] = 0;
        eval(code);
        orderList.length--;
        orderList[orderList.length - 1]++;
        return;
    }
    originCode += '\n//script: ' + orderList.join('_') + '.js\n' + code;
    console.log(orderList.join('_'));
    // fs.writeFileSync(splitF+orderList.join('_')+'.js',code);
    orderList[orderList.length - 1]++;
}
// if(!fs.existsSync(splitF)) {
//     fs.mkdirSync(splitF);
// }
const gameCode = fs.readFileSync(codePath, 'utf8');
if (!gameCode.startsWith(flag)) {
    if (gameCode.includes(flag)) {
        orderList.length++;
        orderList[orderList.length - 1] = 0;
        const parts = gameCode.split(new RegExp(`(${toRegex(flag)}.+${toRegex('.apply(this);')})`, 'gs'));
        if (parts[parts.length - 1] === '') {
            parts.length--;
        }
        for (let i = 0; i < parts.length; i++) {
            if (i % 2 === 0) {
                originCode += parts[i];
                // fs.writeFileSync(splitF+orderList.join('_')+'.js',parts[i]);
            } else {
                encryptFunction(parts[i]);
            }
            orderList[orderList.length - 1]++;
        }
        console.log('file is all decrypted.');
        orderList.length--;
        orderList[orderList.length - 1]++;
    } else {
        originCode = gameCode;
        console.log('file is not encrypted.');
    }
} else {
    encryptFunction(gameCode);
    console.log('file is all decrypted.');
}
fs.writeFileSync(path.join(__dirname, 'decryptedScript.js'), originCode);
