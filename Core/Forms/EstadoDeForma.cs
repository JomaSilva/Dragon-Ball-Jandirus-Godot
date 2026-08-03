namespace Jandirus.Core.Forms;

/// <summary>Por que a transformacao nao rolou. Vazio = rolou.</summary>
public enum RecusaForma
{
	Pode = 0,
	NaoEhSaiyajin,
	SemPoder,        // BP base insuficiente
	SemMaestria,     // grade que ainda nao abriu
	ForaDeOrdem,     // tentou pular degrau
	SemKi,
	Caido,
	JaEsta,
}

/// <summary>
/// A FORMA EM QUE O PERSONAGEM ESTA, e o que ele domina de cada uma.
///
/// Vive no Core porque as duas pontas precisam da MESMA resposta: o cliente pra desenhar a aba
/// Forms (e nao oferecer o que vai ser recusado) e o servidor pra decidir. O que o cliente NAO
/// faz e aplicar o multiplicador -- isso e poder, e poder so o servidor calcula.
/// </summary>
public sealed class EstadoDeForma
{
	/// <summary>
	/// OS LIMIARES DESTE PERSONAGEM. Nulo = usa a constante da escada.
	///
	/// Vive aqui e nao no <see cref="Jandirus.Core.Stats.Fighter"/> porque e dado de FORMA,
	/// nao de corpo -- quem pergunta "posso virar?" ja tem este objeto na mao.
	/// </summary>
	public LimiaresPessoais? Limiares;

	public Forma Atual = Forma.Base;
	public Maestrias Maestria = new();

	/// <summary>Formas que este personagem JA despertou uma vez. E o que dispara a cinematica.</summary>
	public HashSet<int> JaDespertou = [];

	/// <summary>Multiplicador que o <c>Fighter.ssjBuff</c> recebe.</summary>
	public double Multiplicador(bool diluido = false) =>
		Atual == Forma.Base ? 1 : EscadaSaiyajin.Multiplicador(Atual, Maestria, diluido);

	public double DrenoPorSegundo() => EscadaSaiyajin.DrenoPorSegundo(Atual, Maestria);

	/// <summary>
	/// O PROXIMO DEGRAU a partir de onde se esta.
	///
	/// A escada nao e uma lista linear: do SSJ1 saem TRES caminhos (grade 2, grade 3 e o SSJ2), e
	/// a regra e subir pelo mais forte que ja estiver aberto. Quem dominou o SSJ1 (6x) passa
	/// direto pelos grades -- eles ficam obsoletos de proposito, como no original.
	/// </summary>
	public FormaDef? Proxima(double bpBase, bool diluido = false)
	{
		FormaDef? melhor = null;
		double melhorMult = Multiplicador(diluido);

		foreach (FormaDef d in EscadaSaiyajin.Degraus)
		{
			if (d.Id == Atual) continue;
			if (Avaliar(d.Id, bpBase, kiFracao: 1, caido: false, diluido) != RecusaForma.Pode) continue;
			double m = EscadaSaiyajin.Multiplicador(d.Id, Maestria, diluido);
			if (m <= melhorMult) continue;
			melhorMult = m;
			melhor = d;
		}
		return melhor;
	}

	/// <summary>
	/// DA PRA IR PRA ESTA FORMA AGORA?
	///
	/// A ordem das checagens e a do original e ela decide a MENSAGEM: dizer "falta poder" pra
	/// quem so precisa treinar a forma anterior manda a pessoa fazer a coisa errada por horas.
	/// </summary>
	public RecusaForma Avaliar(Forma alvo, double bpBase, double kiFracao, bool caido, bool diluido = false)
	{
		if (alvo == Atual) return RecusaForma.JaEsta;
		if (alvo == Forma.Base) return caido ? RecusaForma.Pode : RecusaForma.Pode;   // descer sempre pode
		if (caido) return RecusaForma.Caido;

		FormaDef? d = EscadaSaiyajin.Def(alvo);
		if (d == null) return RecusaForma.ForaDeOrdem;

		// DEGRAU ANTERIOR. Nao da pra pular: o SSJ2 sai do SSJ1, o SSJ3 do SSJ2. Os grades saem
		// do SSJ1 e o SSJ2 tambem -- por isso `Vem` do SSJ2 e o SSJ1, e nao o grade.
		if (d.Vem != Forma.Base && !EstaEmOuAcimaDe(d.Vem)) return RecusaForma.ForaDeOrdem;

		if (d.PedeMaestria > 0 && Maestria.De(d.Vem) < d.PedeMaestria) return RecusaForma.SemMaestria;

		// A PORTA E O BP BASE. Ver o cabecalho de EscadaSaiyajin -- gatear pelo expresso deixaria
		// a propria forma anterior "fingir" o requisito da seguinte.
		//
		// E A PORTA E DESTE PERSONAGEM, nao da especie: no original cada um sorteia o proprio
		// limiar ao nascer (`statsaiyan.dm:50-56`), e por isso um Saiyajin vira SSJ antes do
		// irmao com o mesmo poder. Sem `Limiares` (personagem velho, ou raca sem sorteio) vale a
		// constante da escada -- o mesmo numero de antes, entao nada quebra.
		double porta = Limiares?.Porta(d.Id) is > 0 and var p ? p : d.PortaBp;
		if (bpBase < porta) return RecusaForma.SemPoder;

		// entrar numa forma no fio do Ki e cair dela no segundo seguinte
		if (kiFracao < 0.1) return RecusaForma.SemKi;
		return RecusaForma.Pode;
	}

	/// <summary>
	/// O degrau exigido ja foi alcancado? Os grades contam como SSJ1 pra quem vem depois -- eles
	/// SAO o SSJ1, so que inchado.
	/// </summary>
	private bool EstaEmOuAcimaDe(Forma exigido)
	{
		if (Atual == exigido) return true;
		if (exigido == Forma.Ssj1 && Atual is Forma.Grade2 or Forma.Grade3) return true;
		return (int)Atual > (int)exigido;
	}

	/// <summary>Entra na forma. Devolve se e a PRIMEIRA vez (a cinematica so roda uma vez).</summary>
	public bool Entrar(Forma f)
	{
		Atual = f;
		if (f == Forma.Base) return false;
		return JaDespertou.Add((int)f);
	}
}
