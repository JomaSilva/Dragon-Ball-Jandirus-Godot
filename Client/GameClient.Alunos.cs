using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Verbos;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// O PAINEL "MESTRE E ALUNOS" NO CLIENTE -- o que o servidor me contou do meu discipulado (`S2C.MestreEAlunos`): o
/// meu mestre e o que ele me ensinou, os colegas, e os alunos que eu guio. Quem desenha e a aba Alunos/Mestre do
/// menu P (`MenuJogo.Alunos.cs`), que tambem e quem pede (ao abrir, e de tempos em tempos enquanto esta aberta).
///
/// E UM CACHE DO QUE O SERVIDOR DECIDIU MANDAR: nao ha nada aqui que a tela precise esconder. Sai tudo quando o
/// personagem muda (<see cref="EsquecerMestreEAlunos"/>) e quando os fatos dizem que o vinculo acabou.
///
/// A FORCA NAO VEM NESTE PACOTE (ver `Net/FioDosAlunos.cs`). O que a tela desenha de "mais forte / mais fraco que
/// voce" e a leitura do sentido de Ki que este cliente JA recebeu (<see cref="Sentidos"/>, o `S2C.Sentidos` da aba
/// Sense) -- ver <see cref="PoderRelativoSentido"/>.
/// </summary>
public partial class GameClient
{
	private PainelDoDiscipulado? _mestreEAlunos;

	/// <summary>SO PRA BANCADA: o painel foi plantado, e nao depende dos fatos que o servidor contou. Ver <see cref="MestreEAlunosDeTeste"/>.</summary>
	private bool _mestreEAlunosPlantado;

	/// <summary>
	/// O meu discipulado, ou nulo se o servidor ainda nao mandou -- ou se eu nao tenho (mais) mestre nem aluno: os
	/// fatos (`S2C.Fatos`) chegam antes de qualquer pedido, e um painel guardado de um vinculo que acabou sai aqui.
	/// </summary>
	public PainelDoDiscipulado? MestreEAlunos
	{
		get
		{
			if (_mestreEAlunos != null && !_mestreEAlunosPlantado && (Fatos & (Fatos.TemMestre | Fatos.TemAluno)) == 0)
			{
				_mestreEAlunos = null;
				VersaoDeMestreEAlunos++;
			}
			return _mestreEAlunos;
		}
	}

	/// <summary>Sobe a cada painel que chega (ou sai). A aba a usa pra saber se o pedido que fez ainda esta a caminho.</summary>
	public int VersaoDeMestreEAlunos { get; private set; }

	/// <summary>
	/// Quando o painel de agora chegou, no relogio desta maquina (`Time.GetTicksMsec`). Os "ha quanto tempo" do pacote
	/// valem pra esse instante: a tela soma o que passou desde entao, e o servidor nao precisa remandar pra o relogio andar.
	/// </summary>
	public ulong MestreEAlunosChegouEmMs { get; private set; }

	public event Action? MestreEAlunosMudou;

	/// <summary>
	/// Pede o painel ao servidor. `seguir` = a tela ja tem um e so quer saber se mudou (o servidor cala se nao mudou).
	/// A resposta, se eu tiver o vinculo, chega no `S2C.MestreEAlunos`.
	/// </summary>
	public void PedirMestreEAlunos(bool seguir) => SendVerbo("alunos", seguir ? "seguir" : "");

	/// <summary>O gesto de um cartao de aluno: guiar AQUELE aluno numa transformacao (o servidor confere se ele esta na frente).</summary>
	public void AjudarAlunoATransformar(string assinatura) => SendVerbo("alunos_ensinar", assinatura);

	/// <summary>O gesto de um cartao de aluno: dispensar AQUELE aluno.</summary>
	public void DispensarAluno(string assinatura) => SendVerbo("alunos_dispensar", assinatura);

	/// <summary>O `S2C.MestreEAlunos` chegou.</summary>
	private void LerMestreEAlunos(NetDataReader reader)
	{
		_mestreEAlunos = FioDosAlunos.Ler(reader);
		_mestreEAlunosPlantado = false;
		MestreEAlunosChegouEmMs = Time.GetTicksMsec();
		VersaoDeMestreEAlunos++;
		MestreEAlunosMudou?.Invoke();
	}

	/// <summary>
	/// ESQUECE o que o servidor contou. Chamado quando um personagem entra no mundo (`S2C.JoinAccepted`): o
	/// `GameClient` atravessa o logout, e o que estava guardado era o discipulado do personagem ANTERIOR.
	/// </summary>
	public void EsquecerMestreEAlunos()
	{
		if (_mestreEAlunos == null) return;
		_mestreEAlunos = null;
		_mestreEAlunosPlantado = false;
		VersaoDeMestreEAlunos++;
	}

	/// <summary>
	/// SO PRA BANCADA (`--diagabas`, F17): planta um painel pelo mesmo evento que o fio dispara, sem servidor por tras
	/// (nulo = desfaz). O painel plantado nao e conferido contra os fatos -- quem diz que ha vinculo, na bancada, e a
	/// `PosseDoCliente.DeTeste`.
	/// </summary>
	public void MestreEAlunosDeTeste(PainelDoDiscipulado? painel)
	{
		_mestreEAlunos = painel;
		_mestreEAlunosPlantado = painel != null;
		MestreEAlunosChegouEmMs = Time.GetTicksMsec();
		VersaoDeMestreEAlunos++;
		MestreEAlunosMudou?.Invoke();
	}

	/// <summary>
	/// **QUANTO ESTA PESSOA TEM DO MEU PODER, PELO MEU SENTIDO DE KI** -- ou nulo se eu nao a estou sentindo agora.
	///
	/// E A LEITURA DA ABA SENSE, e nao uma segunda: a lista e a que o servidor ja me mandou (`GameServer.SentidosDe`),
	/// com as condicoes dele -- so pra quem tem a skill, so de quem esta ao alcance dela, e sem numero nenhum (com o
	/// scouter ligado a lista vira a da aba Scan, a razao nao vem, e esta funcao devolve nulo: a tela de alunos nao
	/// e um medidor). Aqui so se ACHA a pessoa na lista -- ver <see cref="PoderRelativoNaLista"/>.
	/// </summary>
	public float? PoderRelativoSentido(string assinatura, string nome) =>
		PoderRelativoNaLista(Sentidos, SentidosSaoDoScouter, assinatura, nome, Conhecidos.Select(k => (k.Assinatura, k.Nome)));

	/// <summary>
	/// A PROCURA do <see cref="PoderRelativoSentido"/>, sobre listas dadas -- pura, pra bancada poder cobra-la com
	/// listas forjadas.
	///
	/// A lista do sentido de Ki identifica cada presenca de UM de dois jeitos (`GameServer.Sentidos.IdentidadePara`):
	/// quem eu NAO conheco vem pela ASSINATURA (a tela escreve "??? (assinatura)"), e quem eu conheco vem so pelo
	/// NOME. Pela assinatura a pessoa e ela e pronto. Pelo nome e preciso saber DE QUEM e o nome -- e quem sabe e a
	/// minha lista de conhecidos (a da aba People, que tem as duas coisas): a presenca "Gohan" so e o meu aluno se o
	/// UNICO Gohan que eu conheco tem a assinatura dele. Homonimo, nome que eu conheco de outra pessoa, duas
	/// presencas que casam: nao da pra dizer que e ela, e a resposta e nenhuma (melhor nenhuma leitura que a de outro).
	/// </summary>
	internal static float? PoderRelativoNaLista(IReadOnlyList<Protocol.PresencaState> sentidos, bool doScouter, string assinatura, string nome,
												IEnumerable<(string Assinatura, string Nome)> conhecidos)
	{
		if (doScouter || assinatura.Length == 0) return null;

		// O NOME E DELA? So se, entre quem eu conheco, esse nome for de uma pessoa so -- e essa pessoa for ela.
		bool oNomeEDela = false;
		if (nome.Length > 0)
		{
			int comEsseNome = 0;
			foreach ((string sig, string n) in conhecidos)
			{
				if (!string.Equals(n, nome, StringComparison.Ordinal)) continue;
				comEsseNome++;
				oNomeEDela = string.Equals(sig, assinatura, StringComparison.Ordinal);
			}
			oNomeEDela &= comEsseNome == 1;
		}

		float? achado = null;
		int quantas = 0;
		foreach (Protocol.PresencaState p in sentidos)
		{
			bool eEla = p.Assinatura.Length > 0
				? string.Equals(p.Assinatura, assinatura, StringComparison.Ordinal)
				: oNomeEDela && string.Equals(p.Nome, nome, StringComparison.Ordinal);
			if (!eEla) continue;
			quantas++;
			achado = p.PoderRelativo;
		}
		return quantas == 1 && achado is { } r && !float.IsNaN(r) ? r : null;
	}
}
