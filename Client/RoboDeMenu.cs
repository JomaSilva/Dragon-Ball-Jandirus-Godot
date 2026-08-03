using Godot;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO MENU (`--diagmenu`). Abre, percorre todas as abas, fecha, e repete.
///
/// ============================ POR QUE MEDIR ISTO ============================
/// O dono pediu "deixar os menus ja instanciados so que invisiveis... e mais rapido pra engine".
/// Os dois JA nasciam com o mundo e so trocavam `Visible` -- o custo estava noutro lugar: o
/// conteudo era destruido e reconstruido do zero a cada `Redesenhar()`, que roda tambem a cada
/// pacote de ficha (5x por segundo com o menu aberto).
///
/// Sem medida, "cacheei as paginas" e uma afirmacao. Com ela da pra dizer quantas remontagens
/// sobraram, quanto custa cada uma, e quantos pedidos a pagina ja montada atendeu de graca.
/// ===========================================================================
///
/// SEM JANELA, sem mouse: o robo chama `Abrir`/`Fechar` e troca `_aba` pelo mesmo caminho que o
/// botao usa. E o mesmo principio do `--socar`: exercitar a cadeia de verdade, nao uma simulacao.
/// </summary>
public partial class RoboDeMenu : Node
{
	/// <summary>Quantas voltas completas dar antes de imprimir o relatorio.</summary>
	private const int Voltas = 3;

	private double _t;
	private int _passo, _volta;
	private bool _acabou;

	public override void _Process(double delta)
	{
		if (_acabou || MenuJogo.Instancia is not { } m) return;

		_t += delta;
		if (_t < 0.25) return;   // um passo a cada quarto de segundo: da tempo de a ficha chegar
		_t = 0;

		string[] abas = m.AbasDeTeste;

		if (_passo == 0)
		{
			m.Abrir();
			GD.Print($"[menu] volta {_volta + 1}/{Voltas}: abriu");
		}
		else if (_passo <= abas.Length)
		{
			m.IrPara(abas[_passo - 1]);
		}
		else
		{
			m.Fechar();
			_passo = 0;
			if (++_volta >= Voltas)
			{
				_acabou = true;
				GD.Print(m.Relatorio());
			}
			return;
		}
		_passo++;
	}
}
