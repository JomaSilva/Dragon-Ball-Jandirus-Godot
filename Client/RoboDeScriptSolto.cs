using Godot;

namespace Jandirus.Client;

/// <summary>
/// UM NODE QUE NENHUMA LISTA DO JOGO CITA -- o CONTROLE da rodada `--diagestouro --scripts` (o arquivo
/// `RoboDoPrimeiroEstouro.Scripts.cs`), e mais nada: ninguem o instancia fora dela.
///
/// O que a rodada cobra e que o script das classes de efeito tenha DONO, pra o motor nao o carregar de novo a cada
/// nascimento. Este aqui nao tem dono nenhum, de proposito: e nele que a rodada mostra, em toda corrida e no mesmo
/// binario, que o mecanismo continua la -- morta a ultima instancia o Godot solta o script, e o `new` seguinte o
/// carrega outra vez -- e quanto custa a carga MINIMA (uma classe de um metodo, num arquivo de uma tela).
///
/// TEM ARQUIVO PROPRIO PORQUE PRECISA: o gerador do Godot so da caminho de script (`ScriptPath`) a classe que tem o
/// nome do arquivo em que mora, e sem caminho o motor nem passa pelo `ResourceLoader`.
/// </summary>
public partial class RoboDeScriptSolto : Node2D
{
	private double _idade;

	public override void _Process(double delta) => _idade += delta;
}
