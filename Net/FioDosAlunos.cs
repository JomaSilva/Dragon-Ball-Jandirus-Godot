using Jandirus.Core.Skills;
using LiteNetLib.Utils;

namespace Jandirus.Net;

/// <summary>
/// O corpo do `S2C.MestreEAlunos` -- o painel de UM jogador (<see cref="PainelDoDiscipulado"/>), escrito e lido NUM
/// LUGAR SO (o leitor do LiteNetLib nao confere o fim do pacote: um campo a mais num dos lados le lixo). Sem o
/// opcode: quem escreve o pos antes, quem le ja o consumiu no despacho.
///
/// ============================ O QUE VIAJA, E A LISTA E FECHADA DE PROPOSITO ============================
///     bool temMestre
///     [ PESSOA do mestre
///       byte nColegas + nColegas x `string nome`                       (dos colegas so o NOME)
///       byte nEnsinamentos + cada um: ENSINAMENTO (com o MEU progresso)
///                                     + byte nDeles + nDeles x (`byte colega` + PROGRESSO) ]
///     byte nAlunos + cada um: PESSOA + byte nEnsinamentos + nEnsinamentos x ENSINAMENTO
///
///     PESSOA      = `string assinatura` + `string nome` + `string raca` + `bool online` + `uint vistoHa` + `uint desdeHa`
///     ENSINAMENTO = `byte tipo` + `string id` + `uint ha` + `byte tentativas` + `bool assistida` + PROGRESSO
///     PROGRESSO   = `byte etapa` + `ushort nivel` + `ushort teto` + `ushort milesimos`
///
/// O QUE NAO ENTRA, E NAO PODE ENTRAR (dono, 2026-10-11: *"o mestre nao pode ver o bp dos alunos"*;
/// `GameServer.Sigilo.cs`): poder de luta -- nem o numero, nem razao, nem "quanto cresceu" --, atributos, a classe,
/// onde a pessoa esta, o que ela carrega. Nao ha campo de forca NENHUM aqui: a unica leitura de forca que a tela
/// desenha e a do sentido de Ki, que chega (a quem a tem) pelo `S2C.Sentidos` de sempre. Um campo novo neste arquivo
/// e um campo que todo mestre e todo colega passa a ler -- a bancada (`--alunosteste`) confere que o pacote acaba
/// exatamente no ultimo campo desta lista, e que o poder de quem aparece nele nao esta em byte nenhum.
/// ======================================================================================================
/// </summary>
public static class FioDosAlunos
{
	/// <summary>O teto de LEITURA de cada texto (o que passar disso chega vazio, e nao estourado).</summary>
	public const int MaxAssinatura = 32, MaxNome = 64, MaxRaca = 32, MaxId = 160;

	public static void Escrever(NetDataWriter w, PainelDoDiscipulado p)
	{
		w.Put(p.Mestre != null);
		if (p.Mestre is { } m)
		{
			PorPessoa(w, m.Quem);
			int colegas = Math.Min(m.Colegas.Count, byte.MaxValue);
			w.Put((byte)colegas);
			for (int i = 0; i < colegas; i++) w.Put(m.Colegas[i]);

			int n = Math.Min(m.Ensinamentos.Count, byte.MaxValue);
			w.Put((byte)n);
			for (int i = 0; i < n; i++)
			{
				EnsinamentoDoMestre e = m.Ensinamentos[i];
				PorEnsinamento(w, e.Meu);
				int deles = Math.Min(e.Colegas.Count, byte.MaxValue);
				w.Put((byte)deles);
				for (int k = 0; k < deles; k++)
				{
					w.Put(e.Colegas[k].Colega);
					PorProgresso(w, e.Colegas[k].Progresso);
				}
			}
		}

		int alunos = Math.Min(p.Alunos.Count, byte.MaxValue);
		w.Put((byte)alunos);
		for (int i = 0; i < alunos; i++)
		{
			AlunoNoPainel a = p.Alunos[i];
			PorPessoa(w, a.Quem);
			int n = Math.Min(a.Ensinamentos.Count, byte.MaxValue);
			w.Put((byte)n);
			for (int k = 0; k < n; k++) PorEnsinamento(w, a.Ensinamentos[k]);
		}
	}

	public static PainelDoDiscipulado Ler(NetDataReader r)
	{
		MestreNoPainel? mestre = null;
		if (r.GetBool())
		{
			PessoaNoPainel quem = LerPessoa(r);
			int nColegas = r.GetByte();
			var colegas = new List<string>(nColegas);
			for (int i = 0; i < nColegas; i++) colegas.Add(r.GetString(MaxNome));

			int n = r.GetByte();
			var ensinamentos = new List<EnsinamentoDoMestre>(n);
			for (int i = 0; i < n; i++)
			{
				EnsinamentoNoPainel meu = LerEnsinamento(r);
				int nDeles = r.GetByte();
				var deles = new List<ColegaNoEnsinamento>(nDeles);
				for (int k = 0; k < nDeles; k++) deles.Add(new ColegaNoEnsinamento(r.GetByte(), LerProgresso(r)));
				ensinamentos.Add(new EnsinamentoDoMestre(meu, deles));
			}
			mestre = new MestreNoPainel(quem, ensinamentos, colegas);
		}

		int nAlunos = r.GetByte();
		var alunos = new List<AlunoNoPainel>(nAlunos);
		for (int i = 0; i < nAlunos; i++)
		{
			PessoaNoPainel quem = LerPessoa(r);
			int n = r.GetByte();
			var lista = new List<EnsinamentoNoPainel>(n);
			for (int k = 0; k < n; k++) lista.Add(LerEnsinamento(r));
			alunos.Add(new AlunoNoPainel(quem, lista));
		}
		return new PainelDoDiscipulado(mestre, alunos);
	}

	private static void PorPessoa(NetDataWriter w, in PessoaNoPainel q)
	{
		w.Put(q.Assinatura);
		w.Put(q.Nome);
		w.Put(q.Raca);
		w.Put(q.Online);
		w.Put(q.VistoHaSegundos);
		w.Put(q.DesdeHaSegundos);
	}

	private static PessoaNoPainel LerPessoa(NetDataReader r) => new(
		r.GetString(MaxAssinatura), r.GetString(MaxNome), r.GetString(MaxRaca), r.GetBool(), r.GetUInt(), r.GetUInt());

	private static void PorEnsinamento(NetDataWriter w, in EnsinamentoNoPainel e)
	{
		w.Put((byte)e.Tipo);
		w.Put(e.Id);
		w.Put(e.HaSegundos);
		w.Put(e.Tentativas);
		w.Put(e.Assistida);
		PorProgresso(w, e.Progresso);
	}

	private static EnsinamentoNoPainel LerEnsinamento(NetDataReader r) => new(
		(TipoDeEnsinamento)r.GetByte(), r.GetString(MaxId), r.GetUInt(), r.GetByte(), r.GetBool(), LerProgresso(r));

	private static void PorProgresso(NetDataWriter w, in ProgressoNoEnsinamento p)
	{
		w.Put((byte)p.Etapa);
		w.Put(p.Nivel);
		w.Put(p.Teto);
		w.Put(p.Milesimos);
	}

	private static ProgressoNoEnsinamento LerProgresso(NetDataReader r) => new(
		(EtapaDoEnsinamento)r.GetByte(), r.GetUShort(), r.GetUShort(), r.GetUShort());
}
