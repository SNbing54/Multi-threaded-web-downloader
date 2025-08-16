# Multi-threaded-web-downloader
这是一个多线程网络下载器

This is a multi-threaded web downloader

开发者只使用这个下载器到了64线程，不建议再增加，可能被IP封禁

The developer only used this downloader to 64 threads, and it is not recommended to add more, it may be blocked by IP

_**开发者只是一个普通初中生，平时忙于学业，所以还没有编写GUI，请见谅......_**

_**The developer is just an ordinary junior high school student who is usually busy with his studies, so he has not written a GUI yet, please forgive me......**_

各位可以用Microsoft Visual Studio打开Multi-threaded web downloader.sln这个文件，会自动加载出项目

You can open the Multi-threaded web downloader.sln file in Microsoft Visual Studio and it will load the project automatically

**最新版本因需为GUI的开发做准备，所以使用了命令行参数进行调整：**

使用-sdl <URL（用英文双引号标出）>下载文件

使用-smt <下载线程数（阿拉伯数字）> 更改下载线程数量

使用-dff <文件格式（用逗号分隔）> 设置允许Multi-threaded web downloader程序下载的文件格式

使用-out <保存路径（用英文双引号标出）> 设置Multi-threaded web downloader程序下载的文件的保存路径
