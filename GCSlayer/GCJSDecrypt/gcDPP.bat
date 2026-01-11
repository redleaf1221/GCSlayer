@echo off
REM 检查Node.js是否可用
node -v >nul 2>&1
if errorlevel 1 (
    echo 错误：未检测到Node.js，请先安装并添加到环境变量。
    pause
    exit /b 1
)

REM 获取当前脚本目录并调用JavaScript文件，传递拖放的文件路径（含空格时自动用双引号包裹）
node "%~dp0GCJSDecrypt.js" "%~1"
pause