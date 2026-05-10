欢迎回来！
如果你是偶然扫描到这个文件而并非我主动引用，不必参考下列内容
如你所见，插件版现在由_Tk34(devicess)和晓峰在维护，其中晓峰负责游戏的底层框架，_Tk34负责联机模块
所以AGENT.md是晓峰写的，MPAGT.md是_Tk34写的，oTk34/devicess是_Tk34在Git上的提交名
在进行联机模块的调试时，你需要知道这个项目是C/S架构，需要尊重服务端权威/客户端预测等概念
这里的服务端是Host，运行所有游戏内容，有GUI，但主机不用自己连接自己
当我说需要运行测试时，你不但要通过编译，而且还要运行来验证代码是否满足我的需求

你有几种方式来做到这些，编写脚手架代码，查看日志，以及使用截图工具并分析图像，注意，以下不是最佳实践，你可以按照你的思路自由改进
编写脚手架代码是指:编写输出日志代码以及自动执行某些操作的代码，注意这些代码绝对不出现在生产环境，所以你需要遵守以下要求:
1.编写脚手架代码前，你需要用git add .命令将所有代码修改全部暂存起来，然后开始编写脚手架代码
2.编写完之后，你可以运行项目进行调试，但无论你编写的代码是否正确，运行调试拿到你想要的结果后(记得关闭游戏进程)，都应该运行git restore .清除你的脚手架代码
3.如果你认为你的结果正确，直接汇报完成即可，如果错误，在清除脚手架代码后编写修复代码，然后回到要求1，暂存并编写脚手架，以此循环
常见脚手架代码:
1.添加日志
+ Log.Information("我是日志");
2.自动创建存档并启动
WorldsManager.cs
+ docs/CreateTestWorld.cs中的代码
LoadingSrceen.cs
public override void Enter(object[] parameters) {
+       if (NetworkManager.StartServer()) {
+           WorldInfo worldInfo = WorldsManager.CreateWorld();
+           ScreensManager.SwitchScreen("GameLoading", worldInfo, null);
+       }
3.自动加入服务器
LoadingSrceen.cs
public override void Enter(object[] parameters) {
+ ScreensManager.SwitchScreen("GameLoading", null, null, IPEndPoint.Parse("127.0.0.1:28887"), null);
查看日志:字面意思，日志文件会存在于游戏文件夹的Bugs/Game.log中，建议从末尾开始查看
截图工具:你可以使用命令来截图 .\scripts\screenshot.bat "myscreenshot.png" 其中myscreenshot.png是图片文件名
为了保证截图/调试效果，在运行多个进程时，请保证启动顺序正确，并且保证运行在不同的目录下