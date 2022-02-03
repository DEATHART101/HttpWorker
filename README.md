# HttpWorker
拉取B站弹幕的C#脚本，以及一个集成的UnityPackage

HttpWorker是一个典型的生产者-消费者模型，需要由使用者每隔一段时间分配任务HttpWorker.GiveOneWork和拉取任务HttpWorker.ConsumeAll

直接使用UnityPackage的话，可能需要拉入一个NewtonSoft的Dll，然后直接开始游戏就可以啦