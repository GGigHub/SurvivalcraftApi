欢迎回来！
如果你是偶然扫描到这个文件而并非我主动引用，不必参考下列内容
如你所见，插件版现在由_Tk34(devicess)和晓峰在维护，其中晓峰负责游戏的底层框架，_Tk34负责联机模块
所以AGENT.md是晓峰写的，MPAGT.md是_Tk34写的，oTk34/devicess是_Tk34在Git上的提交名
在进行联机模块的调试时，你需要知道这个项目是C/S架构，需要尊重服务端权威/客户端预测等概念
这里的服务端是Host，运行所有游戏内容，有GUI，但主机不用自己连接自己
当我说需要运行测试时，你不但要通过编译，而且还要运行来验证代码是否满足我的需求

你有几种方式来做到这些，编写脚手架代码，查看日志，以及使用截图工具并分析图像，注意，以下不是最佳实践，你可以按照你的思路自由改进
编写脚手架代码是指:编写输出日志代码以及自动执行某些操作的代码
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
注意:
1.为了保证截图/调试效果，在运行多个进程时，请保证启动顺序正确，并且保证运行在不同的目录下
2.每个玩家的OnlineAccessToken不可以相同，否则会直接判定为相同用户然后拒绝连接，当然不建议在代码中改，在游戏文件夹下的docs/Setting.xml配置文件中
3.测试完成后一定要使用截图工具截图，并且读取图片来证明你的任务确实完成了